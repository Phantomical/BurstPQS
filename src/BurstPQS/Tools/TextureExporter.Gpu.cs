using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BurstPQS.Util;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace BurstPQS.Tools;

internal static partial class TextureExporter
{
    #region GPU BC7
    /// <summary>
    /// Encodes BC7 textures with BC7Encode.compute and streams the blocks out to their files.
    /// Every texture being exported shares one per-frame budget, so the game keeps rendering
    /// while it works.
    /// </summary>
    /// <remarks>
    /// Each dispatch covers a run of whole block rows and owns one slot of the pixel and block
    /// buffers until its readback lands. Readbacks are written out in the order they were
    /// requested, so each file's blocks arrive in the order the file stores them.
    ///
    /// Where async compute is supported the dispatches run on a background compute queue.
    /// Readbacks can't be issued from that queue, so a frame's readbacks go on the graphics
    /// queue once the CPU sees the fence after that frame's dispatches pass. Waiting on the
    /// fence on the GPU instead would stall the graphics queue until the dispatches finish.
    /// </remarks>
    static class GpuBC7
    {
        // Consecutive dispatches run one after the other rather than filling the SMs together,
        // so each one has to bring enough warps to hide latency with on its own.
        const int BlocksPerDispatch = 65536;
        const int BlocksPerFrame = BlocksPerDispatch;

        // The full kernel spreads each block over 8 threads of a 64-thread group, so a group
        // covers this many consecutive blocks. Matches BLOCKS_PER_GROUP in BC7Encode.compute.
        const int BlocksPerGroup = 8;

        // A block covers at most 16 pixels, so this holds any dispatch's pixel rows.
        const int SlotPixels = BlocksPerDispatch * 16;

        // Readbacks take a few frames to land, which this comfortably covers at the frame
        // budget, for 40MB of buffers.
        const int Slots = 8;

        static bool loadStarted;
        static bool loadFinished;
        static ComputeShader shader;
        static int fullKernel;
        static int fastKernel;
        static bool asyncCompute;

        static ComputeBuffer pixelBuffer;
        static ComputeBuffer blockBuffer;

        static readonly List<EncodeTask> active = [];
        static readonly Queue<InFlight> inFlight = [];
        static int nextSlot;
        static bool running;

        // Dispatched on the async queue but not yet read back, oldest first.
        static readonly List<InFlight> unrequested = [];

        static readonly int PixelsId = Shader.PropertyToID("_Pixels");
        static readonly int BlocksId = Shader.PropertyToID("_Blocks");
        static readonly int ResXId = Shader.PropertyToID("_ResX");
        static readonly int ResYId = Shader.PropertyToID("_ResY");
        static readonly int BlockRowOffsetId = Shader.PropertyToID("_BlockRowOffset");
        static readonly int BlockRowEndId = Shader.PropertyToID("_BlockRowEnd");
        static readonly int PixelOffsetId = Shader.PropertyToID("_PixelOffset");
        static readonly int BlockOffsetId = Shader.PropertyToID("_BlockOffset");

        internal sealed class InFlight(EncodeTask task, int offset, int size)
        {
            public readonly EncodeTask task = task;

            // Passed once the async queue has finished this entry's dispatch.
            public GraphicsFence fence;

            public bool done;
            public bool abandoned;

            // Left uncreated if the readback failed.
            public NativeArray<byte> data;

            public void Request(CommandBuffer cb) =>
                cb.RequestAsyncReadback(blockBuffer, size, offset, OnComplete);

            void OnComplete(AsyncGPUReadbackRequest request)
            {
                done = true;
                if (abandoned || task.IsFinished || request.hasError)
                    return;

                // The readback's data only lasts until the callback returns.
                data = new NativeArray<byte>(request.GetData<byte>(), Allocator.Persistent);
            }

            public void Abandon()
            {
                abandoned = true;
                if (data.IsCreated)
                    data.Dispose();
            }
        }

        /// <summary>
        /// Whether a texture this wide can be encoded on the GPU. Always false until
        /// <see cref="Load"/> has finished.
        /// </summary>
        public static bool IsAvailable(int resX) =>
            shader != null && (resX + 3) / 4 <= BlocksPerDispatch;

        /// <summary>
        /// Loads the shader the first time it runs, and waits for that load on every run after.
        /// </summary>
        public static IEnumerator Load()
        {
            if (loadStarted)
            {
                while (!loadFinished)
                    yield return null;
                yield break;
            }

            loadStarted = true;
            try
            {
                if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                    yield break;

                var path = BundlePath();
                if (path is null || !File.Exists(path))
                    yield break;

                var bundleRequest = AssetBundle.LoadFromFileAsync(path);
                yield return bundleRequest;

                var bundle = bundleRequest.assetBundle;
                if (bundle == null)
                    yield break;

                var assetRequest = bundle.LoadAssetAsync<ComputeShader>("BC7Encode");
                yield return assetRequest;

                var loaded = assetRequest.asset as ComputeShader;
                if (loaded == null || !FindKernels(loaded))
                    yield break;

                loaded.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                shader = loaded;
                asyncCompute = SystemInfo.supportsAsyncCompute && SystemInfo.supportsGraphicsFence;
            }
            finally
            {
                loadFinished = true;
            }
        }

        static bool FindKernels(ComputeShader loaded)
        {
            try
            {
                fullKernel = loaded.FindKernel("EncodeBC7");
                fastKernel = loaded.FindKernel("EncodeBC7Fast");
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        static string BundlePath()
        {
            var name = Application.platform switch
            {
                RuntimePlatform.WindowsPlayer => "burstpqs-windows.unity3d",
                RuntimePlatform.LinuxPlayer => "burstpqs-linux.unity3d",
                RuntimePlatform.OSXPlayer => "burstpqs-macos.unity3d",
                _ => null,
            };

            if (name is null)
                return null;

            return Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData",
                "BurstPQS",
                "Shaders",
                name
            );
        }

        /// <summary>
        /// Encodes <paramref name="levels"/> as a BC7 DDS at <paramref name="path"/>, once
        /// <paramref name="levelsReady"/> completes. Takes ownership of every level.
        /// </summary>
        public static EncodeTask Encode(
            NativeArray<Color32>[] levels,
            int resX,
            int resY,
            bool fast,
            string path,
            JobHandle levelsReady
        )
        {
            var task = new EncodeTask(levels, resX, resY, fast, path, levelsReady);
            active.Add(task);

            if (!running)
            {
                running = true;
                TextureExportRunner.Instance.StartCoroutine(Run());
            }

            return task;
        }

        static IEnumerator Run()
        {
            var endOfFrame = new WaitForEndOfFrame();

            try
            {
                while (active.Count > 0 || inFlight.Count > 0)
                {
                    yield return endOfFrame;

                    try
                    {
                        CollectReadbacks();
                        DispatchFrame();
                        FinishWritten();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        foreach (var task in active)
                            task.Fail(e);
                        active.Clear();
                        AbandonInFlight();
                    }
                }
            }
            finally
            {
                AbandonInFlight();
                pixelBuffer?.Release();
                blockBuffer?.Release();
                pixelBuffer = null;
                blockBuffer = null;
                nextSlot = 0;
                running = false;
            }
        }

        // Requested readbacks still complete afterwards, so their callbacks have to see this.
        static void AbandonInFlight()
        {
            foreach (var entry in inFlight)
                entry.Abandon();
            inFlight.Clear();
            unrequested.Clear();
        }

        static void CollectReadbacks()
        {
            while (inFlight.Count > 0)
            {
                var entry = inFlight.Peek();
                if (!entry.done)
                    break;

                inFlight.Dequeue();
                entry.task.pending--;

                if (entry.task.IsFinished)
                {
                    entry.Abandon();
                    continue;
                }

                if (!entry.data.IsCreated)
                {
                    entry.task.Fail(new Exception("GPU readback of BC7 blocks failed"));
                    active.Remove(entry.task);
                    continue;
                }

                entry.task.Write(entry.data);
                entry.data = default;
            }
        }

        static void DispatchFrame()
        {
            if (asyncCompute)
                RequestReadbacks();

            CommandBuffer upload = null;
            CommandBuffer encode = null;
            try
            {
                int budget = BlocksPerFrame;
                int firstNew = unrequested.Count;

                foreach (var task in active)
                {
                    while (budget > 0 && inFlight.Count < Slots && task.CanDispatch)
                    {
                        if (encode is null)
                        {
                            EnsureBuffers();
                            encode = BeginEncode(out upload);
                        }

                        int slot = nextSlot;
                        nextSlot = (nextSlot + 1) % Slots;

                        var entry = task.Dispatch(encode, slot, budget, out int blocks);
                        inFlight.Enqueue(entry);
                        budget -= blocks;

                        if (asyncCompute)
                            unrequested.Add(entry);
                        else
                            entry.Request(encode);
                    }

                    if (budget <= 0 || inFlight.Count >= Slots)
                        break;
                }

                if (encode is null)
                    return;

                if (asyncCompute)
                {
                    var fence = encode.CreateAsyncGraphicsFence();
                    for (int i = firstNew; i < unrequested.Count; i++)
                        unrequested[i].fence = fence;

                    Graphics.ExecuteCommandBuffer(upload);
                    Graphics.ExecuteCommandBufferAsync(encode, ComputeQueueType.Background);
                }
                else
                {
                    Graphics.ExecuteCommandBuffer(encode);
                }
            }
            finally
            {
                upload?.Release();
                encode?.Release();
            }
        }

        static CommandBuffer BeginEncode(out CommandBuffer upload)
        {
            var encode = new CommandBuffer { name = "BurstPQS BC7 Encode" };
            upload = null;
            if (!asyncCompute)
                return encode;

            // The upload buffer executes after this frame's SetData calls, so its fence keeps
            // the dispatches from reading pixels before they land.
            upload = new CommandBuffer { name = "BurstPQS BC7 Upload" };
            encode.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
            encode.WaitOnAsyncGraphicsFence(upload.CreateAsyncGraphicsFence());
            return encode;
        }

        // The async queue runs dispatches in order, so the ready entries are a prefix.
        static void RequestReadbacks()
        {
            int ready = 0;
            while (ready < unrequested.Count && unrequested[ready].fence.passed)
                ready++;

            if (ready == 0)
                return;

            var cb = new CommandBuffer { name = "BurstPQS BC7 Readback" };
            try
            {
                for (int i = 0; i < ready; i++)
                    unrequested[i].Request(cb);
                Graphics.ExecuteCommandBuffer(cb);
            }
            finally
            {
                cb.Release();
                unrequested.RemoveRange(0, ready);
            }
        }

        static void FinishWritten()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].TryFinish())
                    active.RemoveAt(i);
            }
        }

        static void EnsureBuffers()
        {
            if (pixelBuffer != null)
                return;

            pixelBuffer = new ComputeBuffer(Slots * SlotPixels, sizeof(uint));
            blockBuffer = new ComputeBuffer(Slots * BlocksPerDispatch, 16);
        }

        /// <summary>
        /// One texture's progress through the encoder. Yield on it to wait for its file to be
        /// written.
        /// </summary>
        public sealed class EncodeTask : CustomYieldInstruction, IDisposable
        {
            NativeArray<Color32>[] levels;
            JobHandle levelsReady;
            readonly int resX;
            readonly int resY;
            readonly bool fast;
            readonly string path;

            FileStream stream;
            ObjectHandle<FileStream> streamHandle;
            JobHandle writes;

            int level;
            int blockRow;
            bool levelsComplete;
            internal int pending;

            public bool IsFinished { get; private set; }
            public Exception Error { get; private set; }

            public override bool keepWaiting => !IsFinished;

            internal EncodeTask(
                NativeArray<Color32>[] levels,
                int resX,
                int resY,
                bool fast,
                string path,
                JobHandle levelsReady
            )
            {
                this.levels = levels;
                this.levelsReady = levelsReady;
                this.resX = resX;
                this.resY = resY;
                this.fast = fast;
                this.path = path;

                stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1 << 16
                );
                streamHandle = new ObjectHandle<FileStream>(stream);
                SaveDdsJob.WriteHeader(stream, DdsFormat.BC7, resX, resY, levels.Length);
            }

            internal bool CanDispatch
            {
                get
                {
                    if (IsFinished || level >= levels.Length)
                        return false;

                    if (!levelsComplete)
                    {
                        if (!levelsReady.IsCompleted)
                            return false;

                        levelsReady.Complete();
                        levelsComplete = true;
                    }

                    return true;
                }
            }

            // Uploads the next run of block rows and records their dispatch into cb, at most
            // budget blocks unless one row is already more than that.
            internal InFlight Dispatch(CommandBuffer cb, int slot, int budget, out int blocks)
            {
                int w = MipSize(resX, level);
                int h = MipSize(resY, level);
                int blocksX = (w + 3) / 4;
                int blocksY = (h + 3) / 4;

                int maxBlocks = Math.Min(budget, BlocksPerDispatch);
                int rows = Math.Max(1, Math.Min(maxBlocks / blocksX, blocksY - blockRow));
                int end = blockRow + rows;
                blocks = rows * blocksX;

                int firstPixelRow = blockRow * 4;
                int pixelRows = Math.Min(end * 4, h) - firstPixelRow;
                int pixelOffset = slot * SlotPixels;
                int blockOffset = slot * BlocksPerDispatch;

                pixelBuffer.SetData(levels[level], firstPixelRow * w, pixelOffset, pixelRows * w);

                int kernel = fast ? fastKernel : fullKernel;
                cb.SetComputeBufferParam(shader, kernel, PixelsId, pixelBuffer);
                cb.SetComputeBufferParam(shader, kernel, BlocksId, blockBuffer);
                cb.SetComputeIntParam(shader, ResXId, w);
                cb.SetComputeIntParam(shader, ResYId, h);
                cb.SetComputeIntParam(shader, BlockRowOffsetId, blockRow);
                cb.SetComputeIntParam(shader, BlockRowEndId, end);
                cb.SetComputeIntParam(shader, PixelOffsetId, pixelOffset);
                cb.SetComputeIntParam(shader, BlockOffsetId, blockOffset);
                if (fast)
                    cb.DispatchCompute(shader, kernel, (blocksX + 7) / 8, (rows + 7) / 8, 1);
                else
                    cb.DispatchCompute(shader, kernel, (blocks + BlocksPerGroup - 1) / BlocksPerGroup, 1, 1);

                var entry = new InFlight(this, blockOffset * 16, blocks * 16);
                pending++;

                blockRow = end;
                if (blockRow == blocksY)
                {
                    // SetData has already copied every row of this level out.
                    levels[level].Dispose();
                    level++;
                    blockRow = 0;
                }

                return entry;
            }

            // Takes ownership of data.
            internal void Write(NativeArray<byte> data)
            {
                writes = new WriteJob { data = data, stream = streamHandle }.Schedule(writes);
                writes = data.Dispose(writes);
                JobHandle.ScheduleBatchedJobs();
            }

            internal bool TryFinish()
            {
                if (IsFinished)
                    return true;
                if (level < levels.Length || pending > 0 || !writes.IsCompleted)
                    return false;

                Close();
                return true;
            }

            internal void Fail(Exception error)
            {
                if (IsFinished)
                    return;

                Error = error;
                Close();
            }

            void Close()
            {
                writes.Complete();
                levelsReady.Complete();

                if (streamHandle.IsAllocated)
                    streamHandle.Dispose();
                stream?.Dispose();
                stream = null;

                foreach (var pixels in levels)
                {
                    if (pixels.IsCreated)
                        pixels.Dispose();
                }
                levels = [];

                IsFinished = true;
            }

            /// <summary>Abandons the encode if it hasn't finished.</summary>
            public void Dispose()
            {
                if (IsFinished)
                    return;

                Fail(new OperationCanceledException());
                active.Remove(this);
            }
        }

        /// <summary>Not burst-compiled since it calls managed I/O APIs.</summary>
        unsafe struct WriteJob : IJob
        {
            [ReadOnly]
            public NativeArray<byte> data;

            public ObjectHandle<FileStream> stream;

            public void Execute()
            {
                using var source = new UnmanagedMemoryStream(
                    (byte*)data.GetUnsafeReadOnlyPtr(),
                    data.Length
                );
                source.CopyTo(stream.Target, 1 << 20);
            }
        }
    }
    #endregion
}
