using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using BurstPQS.Jobs;
using BurstPQS.Util;
using Contracts.Parameters;
using Steamworks;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace BurstPQS.Tools;

public enum HeightFormat
{
    Png,
    R8,
    R16,
    BC4,
}

public enum ColorFormat
{
    Png,
    DXT1,
    DXT5,
    BC7,
    BC7Fast,
}

public enum NormalFormat
{
    Png,
    DXT5nm,
    BC5,
}

/// <summary>
/// How the pixels of an exported texture map onto directions from the centre of the body.
/// </summary>
public enum TextureProjection
{
    /// <summary>A single texture spanning 360 degrees of longitude and 180 of latitude.</summary>
    Equirectangular,

    CubeXP,
    CubeXN,
    CubeYP,
    CubeYN,
    CubeZP,
    CubeZN,
}

internal static class TextureProjections
{
    internal static readonly TextureProjection[] Equirectangular =
    [
        TextureProjection.Equirectangular,
    ];

    /// <summary>The six cube faces, in the order <see cref="CubemapFace"/> declares them.</summary>
    internal static readonly TextureProjection[] CubeFaces =
    [
        TextureProjection.CubeXP,
        TextureProjection.CubeXN,
        TextureProjection.CubeYP,
        TextureProjection.CubeYN,
        TextureProjection.CubeZP,
        TextureProjection.CubeZN,
    ];

    /// <summary>
    /// The suffix that distinguishes one face's files from another's. Empty for
    /// <see cref="TextureProjection.Equirectangular"/>, which produces a single set.
    /// </summary>
    internal static string FileSuffix(this TextureProjection projection) =>
        projection switch
        {
            TextureProjection.CubeXP => "_XP",
            TextureProjection.CubeXN => "_XN",
            TextureProjection.CubeYP => "_YP",
            TextureProjection.CubeYN => "_YN",
            TextureProjection.CubeZP => "_ZP",
            TextureProjection.CubeZN => "_ZN",
            _ => "",
        };
}

internal struct TextureExportOptions
{
    public int resolution;
    public TextureProjection projection;
    public bool exportHeight;
    public bool exportColor;
    public bool exportNormal;
    public HeightFormat heightFormat;
    public ColorFormat colorFormat;
    public NormalFormat normalFormat;
    public bool orientNorthUp;

    public readonly int Width => resolution;

    /// <summary>
    /// An equirectangular map covers twice as much longitude as latitude, so it is half as
    /// tall as it is wide. A cube face is square.
    /// </summary>
    public readonly int Height =>
        projection == TextureProjection.Equirectangular ? resolution / 2 : resolution;
}

internal static partial class TextureExporter
{
    const int BlockSize = 128;

    public static bool IsExporting { get; private set; }
    public static string StatusMessage { get; set; } = "";

    private static CoroutineTask StartTask(IEnumerator routine) =>
        CoroutineTask.Start(TextureExportRunner.Instance, routine);

    /// <summary>
    /// Exports one texture set per entry in <paramref name="projections"/>, then writes the
    /// info.txt describing all of them.
    /// </summary>
    /// <remarks>
    /// Every projection's height maps share one height range, and that range is not known until
    /// the last of them has been computed, so height encoding happens after the loop.
    /// </remarks>
    public static IEnumerator ExportPlanet(
        CelestialBody body,
        TextureExportOptions options,
        TextureProjection[] projections
    )
    {
        var displayName = body.displayName.LocalizeRemoveGender();
        if (body.pqsController == null)
        {
            ScreenMessages.PostScreenMessage(
                $"Body {displayName} has no PQS controller. It will be skipped"
            );
            yield break;
        }

        var exportDir = ExportDirectory(body);
        Directory.CreateDirectory(exportDir);

        if (options.exportColor && options.colorFormat is ColorFormat.BC7 or ColorFormat.BC7Fast)
            yield return GpuBC7.Load();

        using var heightMaps = new HeightMapSet(
            exportDir,
            $"{body.name}_Height",
            options.exportHeight
        );
        using var oceanHeightMaps = new HeightMapSet(
            exportDir,
            $"{body.name}_Height_Ocean",
            options.exportHeight
        );

        var exporters = projections
            .Select(projection =>
            {
                options.projection = projection;
                return new PlanetExporter(body, options, heightMaps, oceanHeightMaps);
            })
            .ToArray();
        using var guard = new ArrayDisposeGuard<PlanetExporter>(exporters);
        // Every exporter has to finish before any error is rethrown, since they share the height
        // maps that unwinding this frame releases.
        var tasks = exporters.Select(exporter => StartTask(exporter.Export())).ToArray();

        foreach (var task in tasks)
            yield return task;

        CoroutineTask.ThrowIfAnyFailed(tasks);

        if (options.exportHeight)
        {
            StatusMessage = $"Exporting {displayName}: saving height maps";
            var task1 = StartTask(heightMaps.Save(options.heightFormat));
            var task2 = StartTask(oceanHeightMaps.Save(options.heightFormat));

            yield return task1;
            yield return task2;

            CoroutineTask.ThrowIfAnyFailed([task1, task2]);
        }

        WriteInfo(body, exportDir, projections, heightMaps, oceanHeightMaps);
    }

    static string ExportDirectory(CelestialBody body) =>
        Path.Combine(KSPUtil.ApplicationRootPath, "PluginData", "BurstPQS", body.name);

    static void WriteInfo(
        CelestialBody body,
        string exportDir,
        TextureProjection[] projections,
        HeightMapSet heightMaps,
        HeightMapSet oceanHeightMaps
    )
    {
        using var writer = new StreamWriter(Path.Combine(exportDir, "info.txt"));

        writer.WriteLine($"planet: {body.name}");
        writer.WriteLine($"projections: {string.Join(" ", projections)}");

        if (heightMaps.HasRange)
        {
            writer.WriteLine($"min height: {heightMaps.Min}");
            writer.WriteLine($"max height: {heightMaps.Max}");
        }

        if (oceanHeightMaps.HasRange)
        {
            writer.WriteLine($"ocean min height: {oceanHeightMaps.Min}");
            writer.WriteLine($"ocean max height: {oceanHeightMaps.Max}");
        }
    }

    struct IsExportingGuard : IDisposable
    {
        public IsExportingGuard() => IsExporting = true;

        public readonly void Dispose() => IsExporting = false;
    }

    struct GameObjectGuard : IDisposable
    {
        public GameObject GameObject { get; private set; }
        private PQ Prefab;

        public GameObjectGuard(GameObject go)
        {
            GameObject = go;

            var prefabGo = new GameObject("PQ (Prefab)");
            prefabGo.SetActive(false);

            GameObject.AddComponent<MeshFilter>().mesh = new Mesh();
            GameObject.AddComponent<MeshRenderer>();
            Prefab = GameObject.AddComponent<PQ>();
        }

        public PQ AddPQ(PQS pqs)
        {
            var quad = GameObject.Instantiate(Prefab, GameObject.transform);
            quad.sphereRoot = pqs;
            quad.subdivision = pqs.maxLevel;
            return quad;
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(GameObject);
            UnityEngine.Object.Destroy(Prefab);
        }
    }

    struct JobSetGuard : IDisposable
    {
        BatchPQSJobSet jobSet;
        ObjectHandle<BatchPQSJobSet> handle;

        public readonly ObjectHandle<BatchPQSJobSet> Handle => handle;
        public readonly JobHandle Dependency => jobSet.Dependency;

        public JobSetGuard(BatchPQS batchPQS, PQ quad)
        {
            jobSet = batchPQS.CreateJobSet(quad);
            handle = new ObjectHandle<BatchPQSJobSet>(jobSet);
        }

        public static JobSetGuard CreateEmpty()
        {
            var guard = default(JobSetGuard);
            guard.jobSet = BatchPQSJobSet.Acquire();
            guard.handle = new ObjectHandle<BatchPQSJobSet>(guard.jobSet);
            return guard;
        }

        public void Dispose()
        {
            if (handle.IsAllocated)
                handle.Dispose();
            jobSet?.Dispose();
        }
    }

    struct BlockState : IDisposable
    {
        public JobHandle handle;
        public JobSetGuard jobSetGuard;
        public JobSetGuard oceanJobSetGuard;
        public int scheduledFrame;

        public bool IsCompleted => handle.IsCompleted;
        public readonly bool HasSpareTime => Time.frameCount - scheduledFrame < 3;

        public void Dispose()
        {
            using (jobSetGuard)
            using (oceanJobSetGuard)
                handle.Complete();
        }
    }

    // Completes whatever is still in flight if the export is abandoned part way through.
    readonly struct BlockQueueGuard(Queue<BlockState> blocks) : IDisposable
    {
        public void Dispose()
        {
            while (blocks.Count > 0)
                blocks.Dequeue().Dispose();
        }
    }

    internal readonly struct ExportGuard : IDisposable
    {
        public ExportGuard() => IsExporting = true;

        public void Dispose()
        {
            IsExporting = false;
            StatusMessage = "";
        }
    }

    readonly struct BuildingMapsGuard : IDisposable
    {
        readonly PQS pqs;
        readonly bool prevPqs;
        readonly PQS oceanPQS;
        readonly bool prevOcean;

        public BuildingMapsGuard(PQS pqs, PQS oceanPQS)
        {
            this.pqs = pqs;
            prevPqs = pqs.isBuildingMaps;
            pqs.isBuildingMaps = true;

            this.oceanPQS = oceanPQS;
            if (oceanPQS != null)
            {
                prevOcean = oceanPQS.isBuildingMaps;
                oceanPQS.isBuildingMaps = true;
            }
            else
            {
                prevOcean = false;
            }
        }

        public void Dispose()
        {
            if (pqs != null)
                pqs.isBuildingMaps = prevPqs;
            if (oceanPQS != null)
                oceanPQS.isBuildingMaps = prevOcean;
        }
    }

    readonly struct FallbackBuildingMapsGuard : IDisposable
    {
        readonly PQS pqs;

        public FallbackBuildingMapsGuard(PQS pqs)
        {
            this.pqs = pqs;
            pqs.isBuildingMaps = true;
            pqs.isFakeBuild = true;
        }

        public void Dispose()
        {
            pqs.isBuildingMaps = false;
            pqs.isFakeBuild = false;
        }
    }

    #region Height maps
    class HeightMapSet(string exportDir, string baseName, bool retain) : IDisposable
    {
        readonly Queue<MappedHeights> maps = [];

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        /// <summary>Whether any projection has reported a range yet.</summary>
        public bool HasRange { get; private set; }

        public float Min => min;
        public float Max => max;

        /// <summary>
        /// Creates the height buffer for one projection. A retained buffer belongs to this set
        /// and must not be disposed by the caller; otherwise it is ordinary memory the caller
        /// owns, since nothing needs to outlive the projection that produced it.
        /// </summary>
        public NativeArray<float> Create(TextureProjection projection, int resX, int resY)
        {
            if (!retain)
                return new NativeArray<float>(
                    resX * resY,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );

            var map = new MappedHeights(exportDir, baseName, projection, resX, resY);
            maps.Enqueue(map);
            return map.Heights;
        }

        /// <summary>Folds one projection's range into the range covering all of them.</summary>
        public void Record(float min, float max)
        {
            this.min = Math.Min(this.min, min);
            this.max = Math.Max(this.max, max);
            HasRange = true;
        }

        /// <summary>
        /// Encodes every projection against the combined range and writes it out. Each
        /// projection releases its own mapping and deletes its own scratch file.
        /// </summary>
        /// <remarks>
        /// An encode is one job, so they are all scheduled before anything is waited on, which
        /// puts as many worker threads on them as there are projections.
        /// </remarks>
        public IEnumerator Save(HeightFormat format)
        {
            var handles = new Queue<JobHandle>();

            foreach (var map in maps)
                handles.Enqueue(map.ScheduleSave(format, min, max));

            JobHandle.ScheduleBatchedJobs();

            while (handles.TryDequeue(out var handle))
            {
                if (!handle.IsCompleted)
                    yield return new WaitUntil(() => handle.IsCompleted);

                handle.Complete();
                maps.Dequeue().Dispose();
            }
        }

        public void Dispose()
        {
            while (maps.TryDequeue(out var map))
                map.Dispose();
        }

        /// <summary>One projection's heights, mapped over a file beside its output.</summary>
        unsafe class MappedHeights : IDisposable
        {
            readonly string outputPath;
            readonly string tempPath;
            readonly int resX;
            readonly int resY;
            readonly MemoryMappedFile file;
            readonly MemoryMappedViewAccessor view;

            bool released;

            public NativeArray<float> Heights { get; }

            public MappedHeights(
                string exportDir,
                string baseName,
                TextureProjection projection,
                int resX,
                int resY
            )
            {
                this.resX = resX;
                this.resY = resY;

                outputPath = Path.Combine(exportDir, baseName + projection.FileSuffix());
                tempPath = outputPath + ".tmp";

                long bytes = (long)resX * resY * sizeof(float);
                file = MemoryMappedFile.CreateFromFile(
                    tempPath,
                    FileMode.Create,
                    null,
                    bytes,
                    MemoryMappedFileAccess.ReadWrite
                );
                view = file.CreateViewAccessor(0, bytes, MemoryMappedFileAccess.ReadWrite);

                byte* pointer = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                Heights = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(
                    pointer + view.PointerOffset,
                    resX * resY,
                    Allocator.Invalid
                );
            }

            /// <summary>
            /// Schedules the encode, the write, and this projection's own release. Takes
            /// ownership: the returned handle covers the release too.
            /// </summary>
            public JobHandle ScheduleSave(HeightFormat format, float minH, float maxH)
            {
                int count = Heights.Length;
                string path = outputPath + (format == HeightFormat.Png ? ".png" : ".dds");
                Debug.Log($"[BurstPQS] Writing {path}");

                if (format == HeightFormat.Png)
                {
                    var pixels = AllocatePixels<Color32>(count);
                    var encodeHandle = new TextureExportEncodeHeightsRGB24Job
                    {
                        heights = Heights,
                        output = pixels,
                        minH = minH,
                        maxH = maxH,
                    }.ScheduleBatch(count, 8192);

                    return ScheduleSaveJob(
                        ref pixels,
                        resX,
                        resY,
                        path,
                        ScheduleRelease(encodeHandle)
                    );
                }

                var ddsFormat = format switch
                {
                    HeightFormat.R8 => DdsFormat.R8,
                    HeightFormat.R16 => DdsFormat.R16,
                    _ => DdsFormat.BC4,
                };

                int mipCount = MipCount(resX, resY);
                var output = AllocatePixels<byte>(MipChainBytes(ddsFormat, resX, resY, mipCount));
                var level = Heights;
                JobHandle handle = default;
                int offset = 0;

                // Everything runs as one chain, since combining handles over a full-size encode
                // runs into the same JobTempAlloc lifetime as ScheduleRelease below.
                for (int mip = 0; mip < mipCount; mip++)
                {
                    int w = MipSize(resX, mip);
                    int h = MipSize(resY, mip);

                    var next = default(NativeArray<float>);
                    if (mip + 1 < mipCount)
                    {
                        int nextW = MipSize(resX, mip + 1);
                        int nextH = MipSize(resY, mip + 1);
                        next = AllocatePixels<float>(nextW * nextH);
                        handle = new TextureExportDownsampleFloatJob
                        {
                            source = level,
                            output = next,
                            srcX = w,
                            srcY = h,
                            dstX = nextW,
                            dstY = nextH,
                        }.ScheduleBatch(nextH, 16, handle);
                    }

                    int bytes = MipLevelBytes(ddsFormat, w, h);
                    handle = ScheduleEncode(
                        format,
                        level,
                        output.GetSubArray(offset, bytes),
                        w,
                        h,
                        minH,
                        maxH,
                        handle
                    );
                    offset += bytes;

                    handle = mip == 0 ? ScheduleRelease(handle) : level.Dispose(handle);
                    level = next;
                }

                return ScheduleSaveDdsJob(
                    ref output,
                    ddsFormat,
                    resX,
                    resY,
                    mipCount,
                    path,
                    handle
                );
            }

            static JobHandle ScheduleEncode(
                HeightFormat format,
                NativeArray<float> heights,
                NativeArray<byte> output,
                int resX,
                int resY,
                float minH,
                float maxH,
                JobHandle dependsOn
            )
            {
                int count = resX * resY;
                switch (format)
                {
                    case HeightFormat.R8:
                        return new TextureExportEncodeHeightsR8Job
                        {
                            heights = heights,
                            output = output,
                            minH = minH,
                            maxH = maxH,
                        }.ScheduleBatch(count, 8192, dependsOn);

                    case HeightFormat.R16:
                        return new TextureExportEncodeHeightsR16Job
                        {
                            heights = heights,
                            output = output.Reinterpret<ushort>(sizeof(byte)),
                            minH = minH,
                            maxH = maxH,
                        }.ScheduleBatch(count, 8192, dependsOn);

                    default:
                        int blocks = BlockCount(resX, resY);
                        return new TextureExportEncodeHeightsBC4Job
                        {
                            heights = heights,
                            output = output.Reinterpret<ulong>(sizeof(byte)),
                            minH = minH,
                            maxH = maxH,
                            resX = resX,
                            resY = resY,
                        }.ScheduleBatch(blocks, 256, dependsOn);
                }
            }

            /// <summary>
            /// Schedules the mapping's release for as soon as the encode has finished reading
            /// it, and returns a handle the write can hang off.
            /// </summary>
            /// <remarks>
            /// The release sits in the write's dependencies rather than being combined with its
            /// handle afterwards. A combined handle is itself a JobTempAlloc allocation that
            /// lives until everything it covers has finished, and compressing a full-size image
            /// runs for far longer than the four frames that allocator permits.
            /// </remarks>
            JobHandle ScheduleRelease(JobHandle encodeHandle)
            {
                var self = new ObjectHandle<MappedHeights>(this);
                var handle = new ReleaseJob { map = self }.Schedule(encodeHandle);
                return self.Dispose(handle);
            }

            /// <summary>
            /// Closes a mapping once the encode has read it. Managed cleanup, so it is not
            /// burst-compiled.
            /// </summary>
            struct ReleaseJob : IJob
            {
                public ObjectHandle<MappedHeights> map;

                public void Execute() => map.Target.Release();
            }

            /// <summary>
            /// Closes the mapping, which is all this needs to do before the encode's pages can
            /// go back to the OS.
            /// </summary>
            /// <remarks>
            /// Deleting the file is left to <see cref="Dispose"/> on the main thread. KSP's
            /// debug console handles log callbacks by touching its own UI, so a warning raised
            /// from a worker thread takes the game down with it.
            /// </remarks>
            public void Release()
            {
                if (released)
                    return;

                released = true;
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                view.Dispose();
                file.Dispose();
            }

            public void Dispose()
            {
                Release();

                // A scratch file left behind is not worth failing a finished export over, and
                // the next export overwrites it anyway.
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[BurstPQS] Could not delete {tempPath}: {e.Message}");
                }
            }
        }
    }
    #endregion

    #region Exporter
    class PlanetExporter : IDisposable
    {
        readonly PQS pqs;
        readonly BatchPQS batchPQS;
        readonly CelestialBody body;
        readonly TextureExportOptions options;
        readonly string bodyName;

        readonly bool hasOcean;
        readonly PQS oceanPQS;
        readonly BatchPQS oceanBatchPQS;

        readonly HeightMapSet heightMaps;
        readonly HeightMapSet oceanHeightMaps;

        readonly float clampMin;
        readonly float clampMax;
        readonly float oceanClampMin;
        readonly float oceanClampMax;

        Permit permit;

        NativeArray<float> heights;
        NativeArray<Color32> normals;
        NativeArray<Color32> colors;
        NativeArray<float> blockMinMax;

        NativeArray<float> oceanHeights;
        NativeArray<Color32> oceanNormals;
        NativeArray<Color32> oceanColors;
        NativeArray<float> oceanBlockMinMax;

        readonly List<GpuBC7.EncodeTask> gpuTasks = [];

        float minH;
        float maxH;
        float oceanMinH;
        float oceanMaxH;

        public PlanetExporter(
            CelestialBody body,
            TextureExportOptions options,
            HeightMapSet heightMaps,
            HeightMapSet oceanHeightMaps
        )
        {
            this.body = body;
            this.options = options;
            this.heightMaps = heightMaps;
            this.oceanHeightMaps = oceanHeightMaps;
            bodyName = body.bodyName.LocalizeRemoveGender();

            pqs = body.pqsController;
            batchPQS = pqs.GetComponent<BatchPQS>();
            if (batchPQS?.Fallback ?? true)
                batchPQS = null;

            hasOcean = body.ocean && pqs.ChildSpheres is { Length: > 0 };
            oceanPQS = hasOcean ? pqs.ChildSpheres[0] : null;
            oceanBatchPQS = oceanPQS?.GetComponent<BatchPQS>();

            if (oceanBatchPQS?.Fallback ?? true)
                oceanBatchPQS = null;

            clampMin = (float)(pqs.radiusMin - pqs.radius);
            clampMax = (float)(pqs.radiusMax - pqs.radius);

            if (hasOcean)
            {
                oceanClampMin = Math.Max(clampMin, (float)(oceanPQS.radiusMin - pqs.radius));
                oceanClampMax = Math.Max(clampMax, (float)(oceanPQS.radiusMax - pqs.radius));
            }
        }

        public IEnumerator Export()
        {
            yield return ExecuteComputeJobs();

            using (var minMax = new NativeArray<float>(2, Allocator.TempJob))
            {
                yield return ComputeMinMax(blockMinMax, minMax);
                minH = minMax[0];
                maxH = minMax[1];
            }

            if (hasOcean)
            {
                using var minMax = new NativeArray<float>(2, Allocator.TempJob);
                yield return ComputeMinMax(oceanBlockMinMax, minMax);
                oceanMinH = minMax[0];
                oceanMaxH = minMax[1];
            }

            yield return ExportTextures();
        }

        private IEnumerator ExecuteComputeJobs()
        {
            permit = new Permit();
            if (!permit.IsActive)
                yield return permit;

            int resX = options.Width;
            int resY = options.Height;
            int pixelCount = resX * resY;
            int numBlocksX = (resX + BlockSize - 1) / BlockSize;
            int numBlocksY = (resY + BlockSize - 1) / BlockSize;
            int totalBlocks = numBlocksX * numBlocksY;

            // Create a PQ under a disabled GameObject for OnQuadPreBuild.
            using var goGuard = new GameObjectGuard(new GameObject("BurstPQS_TextureExportQuad"));
            goGuard.GameObject.SetActive(false);
            var quad = goGuard.AddPQ(pqs);

            heights = heightMaps.Create(options.projection, resX, resY);
            blockMinMax = CreateBlockMinMax(totalBlocks);
            normals = new NativeArray<Color32>(
                pixelCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            colors = new NativeArray<Color32>(
                pixelCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            using var oceanGoGuard = hasOcean
                ? new GameObjectGuard(new GameObject("BurstPQS_TextureExportOceanQuad"))
                : default;
            PQ oceanQuad = null;
            if (hasOcean)
            {
                oceanGoGuard.GameObject.SetActive(false);
                oceanQuad = oceanGoGuard.AddPQ(oceanPQS);
            }

            if (hasOcean)
            {
                oceanHeights = oceanHeightMaps.Create(options.projection, resX, resY);
                oceanBlockMinMax = CreateBlockMinMax(totalBlocks);
                oceanNormals = new NativeArray<Color32>(
                    pixelCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
                oceanColors = new NativeArray<Color32>(
                    pixelCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
            }

            var sphere = new SphereData(pqs);
            var oceanSphere = hasOcean ? new SphereData(oceanPQS) : default;
            var oceanColor = hasOcean ? pqs.mapOceanColor : default;

            // Schedule block jobs in bounded batches so that worker threads aren't
            // fully saturated. The normal PQS update cycle uses fire-and-forget
            // TempJob dispose jobs; if we flood the job queue, those dispose jobs
            // can't run within their 4-frame lifetime, producing warnings.
            int maxInFlight = Math.Max(JobsUtility.JobWorkerCount * 2, 4);
            var blocks = new Queue<BlockState>();
            using var blocksGuard = new BlockQueueGuard(blocks);
            int complete = 0;

            var lastYield = Time.realtimeSinceStartup;

            for (int by = 0; by < numBlocksY; by++)
            {
                for (int bx = 0; bx < numBlocksX; bx++)
                {
                    // Create a fresh job set for this block so OnQuadPreBuild
                    // is called per-block, allowing mods to set up per-quad state.
                    var block = new BlockState();
                    var handle = ComputeBlock(
                        bx,
                        by,
                        by * numBlocksX + bx,
                        ref block,
                        quad,
                        oceanQuad,
                        sphere,
                        oceanSphere
                    );

                    block.handle = handle;
                    block.scheduledFrame = Time.frameCount;
                    blocks.Enqueue(block);

                    bool yielded = false;

                    // Wait for room whenever more blocks are in flight than the workers are
                    // keeping up with.
                    if (blocks.Count >= maxInFlight)
                    {
                        JobHandle.ScheduleBatchedJobs();

                        while (true)
                        {
                            complete += DrainReady(blocks, ref maxInFlight);
                            if (blocks.Count < maxInFlight)
                                break;

                            StatusMessage =
                                $"Exporting {bodyName}: computed {complete}/{totalBlocks} chunks";
                            yielded = true;
                            yield return null;
                        }
                    }

                    if (yielded)
                    {
                        lastYield = Time.realtimeSinceStartup;
                    }
                    else if (Time.realtimeSinceStartup - lastYield > 0.1)
                    {
                        yield return null;
                        lastYield = Time.realtimeSinceStartup;
                    }

                    complete += DrainReady(blocks, ref maxInFlight);
                }

                // Flush remaining scheduled jobs and drain the queue.
                JobHandle.ScheduleBatchedJobs();
                while (blocks.Count > 0)
                {
                    complete += DrainReady(blocks, ref maxInFlight);
                    if (blocks.Count == 0)
                        break;

                    StatusMessage =
                        $"Exporting {bodyName}: computed {complete}/{totalBlocks} chunks";
                    yield return null;
                }

                StatusMessage = $"Exporting {bodyName}: computed {complete}/{totalBlocks} chunks";
            }
        }

        static int DrainReady(Queue<BlockState> blocks, ref int maxInFlight)
        {
            int drained = 0;

            while (blocks.TryPeek(out var block))
            {
                if (!block.IsCompleted && block.HasSpareTime)
                    break;

                if (block.IsCompleted)
                    maxInFlight += 1;
                else
                    maxInFlight = Math.Max(
                        JobsUtility.JobWorkerCount,
                        maxInFlight - maxInFlight / 4
                    );

                blocks.Dequeue();
                block.Dispose();
                drained++;
            }

            return drained;
        }

        static NativeArray<float> CreateBlockMinMax(int totalBlocks)
        {
            var array = new NativeArray<float>(
                totalBlocks * 2,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            for (int i = 0; i < totalBlocks; i++)
            {
                array[i * 2] = float.PositiveInfinity;
                array[i * 2 + 1] = float.NegativeInfinity;
            }

            return array;
        }

        private IEnumerator ComputeMinMax(NativeArray<float> blockMinMax, NativeArray<float> minMax)
        {
            var handle = new ReduceMinMaxJob
            {
                blockMinMax = blockMinMax,
                minMax = minMax,
            }.Schedule();

            yield return null;

            handle.Complete();
        }

        private IEnumerator ExportTextures()
        {
            heightMaps.Record(minH, maxH);
            if (hasOcean)
                oceanHeightMaps.Record(oceanMinH, oceanMaxH);

            Debug.Log(
                $"[BurstPQS] {body.name} {options.projection}: min altitude = {minH:F1}m, max altitude = {maxH:F1}m"
            );
            if (hasOcean)
                Debug.Log(
                    $"[BurstPQS] {body.name} {options.projection}: ocean min = {oceanMinH:F1}m, ocean max = {oceanMaxH:F1}m"
                );

            JobHandle handle = default;
            int resX = options.Width;
            int resY = options.Height;
            int pixelCount = resX * resY;

            // Flip textures vertically so north is at the top of the image.
            if (options.orientNorthUp)
            {
                int halfRows = resY / 2;

                var flipH = new TextureExportFlipFloatJob
                {
                    data = heights,
                    resX = resX,
                    resY = resY,
                }.ScheduleBatch(halfRows, 64);

                var flipN = new TextureExportFlipColor32Job
                {
                    data = normals,
                    resX = resX,
                    resY = resY,
                }.ScheduleBatch(halfRows, 64);

                var flipC = new TextureExportFlipColor32Job
                {
                    data = colors,
                    resX = resX,
                    resY = resY,
                }.ScheduleBatch(halfRows, 64);

                handle = JobHandle.CombineDependencies(flipH, flipN, flipC);

                if (hasOcean)
                {
                    var flipOH = new TextureExportFlipFloatJob
                    {
                        data = oceanHeights,
                        resX = resX,
                        resY = resY,
                    }.ScheduleBatch(halfRows, 64);

                    var flipON = new TextureExportFlipColor32Job
                    {
                        data = oceanNormals,
                        resX = resX,
                        resY = resY,
                    }.ScheduleBatch(halfRows, 64);

                    var flipOC = new TextureExportFlipColor32Job
                    {
                        data = oceanColors,
                        resX = resX,
                        resY = resY,
                    }.ScheduleBatch(halfRows, 64);

                    handle = JobHandle.CombineDependencies(
                        handle,
                        JobHandle.CombineDependencies(flipOH, flipON, flipOC)
                    );
                }

                JobHandle.ScheduleBatchedJobs();
            }

            permit?.Dispose();
            permit = null;

            var handles = new Queue<JobHandle>();
            string suffix = options.projection.FileSuffix();
            string exportDir = ExportDirectory(body);

            if (options.exportNormal)
            {
                var normalEncoding = options.normalFormat switch
                {
                    NormalFormat.DXT5nm => BlockEncoding.DXT5nm,
                    NormalFormat.BC5 => BlockEncoding.BC5,
                    _ => (BlockEncoding?)null,
                };

                var normalHandle = SaveColors(
                    ref normals,
                    normalEncoding,
                    exportDir,
                    $"{body.name}_Normal{suffix}",
                    handle
                );
                handles.Enqueue(normalHandle);

                if (hasOcean)
                {
                    var oceanNormalHandle = SaveColors(
                        ref oceanNormals,
                        normalEncoding,
                        exportDir,
                        $"{body.name}_Normal_Ocean{suffix}",
                        handle
                    );
                    handles.Enqueue(oceanNormalHandle);
                }
            }
            else
            {
                normals.Dispose(default);
                oceanNormals.Dispose(default);
            }

            if (options.exportColor)
            {
                var colorEncoding = options.colorFormat switch
                {
                    ColorFormat.DXT1 => BlockEncoding.BC1,
                    ColorFormat.DXT5 => BlockEncoding.BC3,
                    ColorFormat.BC7 => BlockEncoding.BC7,
                    ColorFormat.BC7Fast => BlockEncoding.BC7Fast,
                    _ => (BlockEncoding?)null,
                };

                if (
                    colorEncoding is BlockEncoding.BC7 or BlockEncoding.BC7Fast
                    && GpuBC7.IsAvailable(resX)
                )
                {
                    bool fast = colorEncoding == BlockEncoding.BC7Fast;
                    gpuTasks.Add(
                        SaveColorsGpu(
                            ref colors,
                            fast,
                            exportDir,
                            $"{body.name}_Color{suffix}",
                            handle
                        )
                    );

                    if (hasOcean)
                    {
                        gpuTasks.Add(
                            SaveColorsGpu(
                                ref oceanColors,
                                fast,
                                exportDir,
                                $"{body.name}_Color_Ocean{suffix}",
                                handle
                            )
                        );
                    }
                }
                else
                {
                    var colorHandle = SaveColors(
                        ref colors,
                        colorEncoding,
                        exportDir,
                        $"{body.name}_Color{suffix}",
                        handle
                    );
                    handles.Enqueue(colorHandle);

                    if (hasOcean)
                    {
                        var oceanColorHandle = SaveColors(
                            ref oceanColors,
                            colorEncoding,
                            exportDir,
                            $"{body.name}_Color_Ocean{suffix}",
                            handle
                        );
                        handles.Enqueue(oceanColorHandle);
                    }
                }
            }
            else
            {
                colors.Dispose(default);
                oceanColors.Dispose(default);
            }

            if (options.exportHeight)
            {
                // The maps themselves are encoded once every projection has reported its range.
                // Queueing the flip keeps the wait below covering the mapped buffers.
                handles.Enqueue(handle);
            }
            else
            {
                heights.Dispose(default);
                oceanHeights.Dispose(default);
            }

            JobHandle.ScheduleBatchedJobs();

            StatusMessage = $"Exporting {bodyName}: compressing and saving textures";
            while (handles.TryDequeue(out handle))
            {
                if (!handle.IsCompleted)
                    yield return new WaitUntil(() => handle.IsCompleted);

                handle.Complete();
            }

            foreach (var task in gpuTasks)
            {
                yield return task;

                if (task.Error is not null)
                    throw new Exception($"Failed to encode {bodyName} on the GPU", task.Error);
            }
        }

        private JobHandle ComputeBlock(
            int bx,
            int by,
            int index,
            ref BlockState block,
            PQ quad,
            PQ oceanQuad,
            SphereData sphere,
            SphereData oceanSphere
        )
        {
            int resX = options.Width;
            int resY = options.Height;

            int startX = bx * BlockSize;
            int startY = by * BlockSize;
            int blockW = Math.Min(BlockSize, resX - startX);
            int blockH = Math.Min(BlockSize, resY - startY);
            int blockSize = blockW * blockH;

            // Terrain block
            var blockHeights = new NativeArray<float>(
                blockSize,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );
            var blockNormals = new NativeArray<Vector3>(
                blockSize,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );
            var blockColors = new NativeArray<Color>(
                blockSize,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );

            using (new BuildingMapsGuard(pqs, oceanPQS))
            {
                block.jobSetGuard = batchPQS is not null
                    ? new JobSetGuard(batchPQS, quad)
                    : JobSetGuard.CreateEmpty();

                if (hasOcean)
                {
                    block.oceanJobSetGuard = oceanBatchPQS is not null
                        ? new JobSetGuard(oceanBatchPQS, oceanQuad)
                        : JobSetGuard.CreateEmpty();
                }
            }

            var blockJob = new TextureExportBlockJob
            {
                jobSet = block.jobSetGuard.Handle,
                sphere = sphere,
                projection = options.projection,
                resX = resX,
                resY = resY,
                startX = startX,
                startY = startY,
                blockW = blockW,
                blockH = blockH,
                blockHeights = blockHeights,
                blockNormals = blockNormals,
                blockColors = blockColors,
            };

            JobHandle handle;
            if (batchPQS is not null)
            {
                handle = blockJob.Schedule(block.jobSetGuard.Dependency);
            }
            else
            {
                try
                {
                    using (new FallbackBuildingMapsGuard(pqs))
                        blockJob.ExecuteFallback(pqs, quad);
                }
                catch
                {
                    blockHeights.Dispose();
                    blockNormals.Dispose();
                    blockColors.Dispose();
                    block.Dispose();
                    throw;
                }

                handle = block.jobSetGuard.Dependency;
            }

            // Copy terrain data to terrain output arrays.
            var terrainCopyH = new TextureExportCopyHeightsJob
            {
                blockHeights = blockHeights,
                outputHeights = heights,
                blockMinMax = blockMinMax,
                block = index,
                clampMin = clampMin,
                clampMax = clampMax,
                resX = resX,
                startX = startX,
                startY = startY,
                blockW = blockW,
                blockH = blockH,
            }.Schedule(handle);

            var terrainCopyN = new TextureExportCopyNormalsJob
            {
                blockNormals = blockNormals,
                outputNormals = normals,
                resX = resX,
                startX = startX,
                startY = startY,
                blockW = blockW,
                blockH = blockH,
            }.Schedule(handle);

            var terrainCopyC = new TextureExportCopyColorsJob
            {
                blockColors = blockColors,
                outputColors = colors,
                resX = resX,
                startX = startX,
                startY = startY,
                blockW = blockW,
                blockH = blockH,
            }.Schedule(handle);

            handle = JobHandle.CombineDependencies(terrainCopyH, terrainCopyN, terrainCopyC);

            if (hasOcean)
            {
                // Ocean PQS
                var oceanBlockHeights = new NativeArray<float>(
                    blockSize,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory
                );
                var oceanBlockNormals = new NativeArray<Vector3>(
                    blockSize,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory
                );

                var oceanJob = new TextureExportOceanBlockJob
                {
                    jobSet = block.oceanJobSetGuard.Handle,
                    sphere = oceanSphere,
                    projection = options.projection,
                    resX = resX,
                    resY = resY,
                    startX = startX,
                    startY = startY,
                    blockW = blockW,
                    blockH = blockH,
                    blockHeights = oceanBlockHeights,
                    blockNormals = oceanBlockNormals,
                };

                handle = JobHandle.CombineDependencies(
                    handle,
                    block.oceanJobSetGuard.Dependency
                );

                if (oceanBatchPQS is not null)
                {
                    handle = oceanJob.Schedule(handle);
                }
                else
                {
                    try
                    {
                        using (new FallbackBuildingMapsGuard(oceanPQS))
                            oceanJob.ExecuteFallback(oceanPQS, oceanQuad);
                    }
                    catch
                    {
                        // The terrain copies are already reading these.
                        handle.Complete();
                        blockHeights.Dispose();
                        blockNormals.Dispose();
                        blockColors.Dispose();
                        oceanBlockHeights.Dispose();
                        oceanBlockNormals.Dispose();
                        block.Dispose();
                        throw;
                    }
                }

                handle = new TextureExportBlendOceanJob
                {
                    blockHeights = blockHeights,
                    blockNormals = blockNormals,
                    blockColors = blockColors,
                    oceanHeights = oceanBlockHeights,
                    oceanNormals = oceanBlockNormals,
                    oceanColor = hasOcean ? pqs.mapOceanColor : default,
                }.Schedule(handle);

                // Copy blended data to ocean output arrays.
                var oceanCopyH = new TextureExportCopyHeightsJob
                {
                    blockHeights = blockHeights,
                    outputHeights = oceanHeights,
                    blockMinMax = oceanBlockMinMax,
                    block = index,
                    clampMin = oceanClampMin,
                    clampMax = oceanClampMax,
                    resX = resX,
                    startX = startX,
                    startY = startY,
                    blockW = blockW,
                    blockH = blockH,
                }.Schedule(handle);

                var oceanCopyN = new TextureExportCopyNormalsJob
                {
                    blockNormals = blockNormals,
                    outputNormals = oceanNormals,
                    resX = resX,
                    startX = startX,
                    startY = startY,
                    blockW = blockW,
                    blockH = blockH,
                }.Schedule(handle);

                var oceanCopyC = new TextureExportCopyColorsJob
                {
                    blockColors = blockColors,
                    outputColors = oceanColors,
                    resX = resX,
                    startX = startX,
                    startY = startY,
                    blockW = blockW,
                    blockH = blockH,
                }.Schedule(handle);

                handle = JobHandle.CombineDependencies(oceanCopyH, oceanCopyN, oceanCopyC);

                handle = JobHandle.CombineDependencies(
                    oceanBlockHeights.Dispose(handle),
                    oceanBlockNormals.Dispose(handle)
                );
            }

            // The frees are part of the block's handle rather than fire-and-forget jobs, so that
            // completing a block actually releases its TempJob buffers. Left to run on their own
            // they queue up behind whatever else is scheduled, and a buffer freed past its
            // four-frame lifetime makes the engine log from the worker thread doing the freeing.
            return JobHandle.CombineDependencies(
                blockHeights.Dispose(handle),
                blockNormals.Dispose(handle),
                blockColors.Dispose(handle)
            );
        }

        /// <summary>
        /// Writes <paramref name="source"/> as a PNG, or as a DDS compressed with
        /// <paramref name="encoding"/>. Takes ownership of <paramref name="source"/>.
        /// </summary>
        JobHandle SaveColors(
            ref NativeArray<Color32> source,
            BlockEncoding? encoding,
            string exportDir,
            string baseName,
            JobHandle dependsOn = default
        )
        {
            int resX = options.Width;
            int resY = options.Height;

            if (encoding is not BlockEncoding enc)
            {
                var pngPath = Path.Combine(exportDir, baseName + ".png");
                Debug.Log($"[BurstPQS] Writing {pngPath}");

                return ScheduleSaveJob(ref source, resX, resY, pngPath, dependsOn);
            }

            var path = Path.Combine(exportDir, baseName + ".dds");
            Debug.Log($"[BurstPQS] Writing {path}");

            var format = enc switch
            {
                BlockEncoding.BC1 => DdsFormat.BC1,
                BlockEncoding.BC5 => DdsFormat.BC5,
                BlockEncoding.BC7 or BlockEncoding.BC7Fast => DdsFormat.BC7,
                _ => DdsFormat.BC3,
            };

            int mipCount = MipCount(resX, resY);
            var compressed = AllocatePixels<byte>(MipChainBytes(format, resX, resY, mipCount));
            var level = source;
            source = default;
            var handle = dependsOn;
            int offset = 0;

            // A single chain, for the same JobTempAlloc reason as the heights.
            for (int mip = 0; mip < mipCount; mip++)
            {
                int w = MipSize(resX, mip);
                int h = MipSize(resY, mip);

                var next = default(NativeArray<Color32>);
                if (mip + 1 < mipCount)
                {
                    int nextW = MipSize(resX, mip + 1);
                    int nextH = MipSize(resY, mip + 1);
                    next = AllocatePixels<Color32>(nextW * nextH);
                    handle = new TextureExportDownsampleColor32Job
                    {
                        source = level,
                        output = next,
                        srcX = w,
                        srcY = h,
                        dstX = nextW,
                        dstY = nextH,
                    }.ScheduleBatch(nextH, 16, handle);
                }

                int bytes = MipLevelBytes(format, w, h);
                handle = new TextureExportCompressJob
                {
                    pixels = level,
                    output = compressed.GetSubArray(offset, bytes),
                    encoding = enc,
                    resX = w,
                    resY = h,
                }.ScheduleBatch(BlockCount(w, h), 64, handle);
                offset += bytes;

                handle = level.Dispose(handle);
                level = next;
            }

            return ScheduleSaveDdsJob(ref compressed, format, resX, resY, mipCount, path, handle);
        }

        /// <summary>
        /// Writes <paramref name="source"/> as a BC7 DDS encoded on the GPU. The mip levels are
        /// still downsampled on the CPU. Takes ownership of <paramref name="source"/>.
        /// </summary>
        GpuBC7.EncodeTask SaveColorsGpu(
            ref NativeArray<Color32> source,
            bool fast,
            string exportDir,
            string baseName,
            JobHandle dependsOn
        )
        {
            int resX = options.Width;
            int resY = options.Height;

            var path = Path.Combine(exportDir, baseName + ".dds");
            Debug.Log($"[BurstPQS] Writing {path}");

            var levels = new NativeArray<Color32>[MipCount(resX, resY)];
            levels[0] = source;
            source = default;
            var handle = dependsOn;

            for (int mip = 1; mip < levels.Length; mip++)
            {
                int w = MipSize(resX, mip);
                int h = MipSize(resY, mip);
                levels[mip] = AllocatePixels<Color32>(w * h);
                handle = new TextureExportDownsampleColor32Job
                {
                    source = levels[mip - 1],
                    output = levels[mip],
                    srcX = MipSize(resX, mip - 1),
                    srcY = MipSize(resY, mip - 1),
                    dstX = w,
                    dstY = h,
                }.ScheduleBatch(h, 16, handle);
            }

            return GpuBC7.Encode(levels, resX, resY, fast, path, handle);
        }

        public void Dispose()
        {
            permit?.Dispose();

            foreach (var task in gpuTasks)
                task.Dispose();

            blockMinMax.Dispose();
            oceanBlockMinMax.Dispose();

            normals.Dispose();
            colors.Dispose();
            oceanNormals.Dispose();
            oceanColors.Dispose();

            // Retained height buffers belong to the HeightMapSet, which releases them once it
            // has encoded every projection.
            if (!options.exportHeight)
            {
                heights.Dispose();
                oceanHeights.Dispose();
            }
        }
    }

    #endregion

    #region File I/O
    static NativeArray<T> AllocatePixels<T>(int length)
        where T : struct =>
        new(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

    static int MipCount(int resX, int resY)
    {
        int count = 1;
        for (int size = Math.Max(resX, resY); size > 1; size >>= 1)
            count++;
        return count;
    }

    static int MipSize(int res, int mip) => Math.Max(1, res >> mip);

    static int BlockCount(int resX, int resY) => (resX + 3) / 4 * ((resY + 3) / 4);

    static int MipLevelBytes(DdsFormat format, int resX, int resY) =>
        format switch
        {
            DdsFormat.R8 => resX * resY,
            DdsFormat.R16 => resX * resY * 2,
            DdsFormat.BC1 or DdsFormat.BC4 => BlockCount(resX, resY) * 8,
            _ => BlockCount(resX, resY) * 16,
        };

    static int MipChainBytes(DdsFormat format, int resX, int resY, int mipCount)
    {
        int bytes = 0;
        for (int mip = 0; mip < mipCount; mip++)
            bytes += MipLevelBytes(format, MipSize(resX, mip), MipSize(resY, mip));
        return bytes;
    }

    /// <summary>Takes ownership of <paramref name="pixels"/>.</summary>
    static JobHandle ScheduleSaveJob(
        ref NativeArray<Color32> pixels,
        int resX,
        int resY,
        string path,
        JobHandle dependency
    )
    {
        var pathHandle = new ObjectHandle<string>(path);
        var handle = new SavePngJob<Color32>
        {
            pixels = pixels,
            format = GraphicsFormat.R8G8B8A8_UNorm,
            resX = resX,
            resY = resY,
            path = pathHandle,
        }.Schedule(dependency);

        handle = pathHandle.Dispose(handle);
        return pixels.Dispose(handle);
    }

    /// <summary>Takes ownership of <paramref name="data"/>.</summary>
    static JobHandle ScheduleSaveDdsJob<T>(
        ref NativeArray<T> data,
        DdsFormat format,
        int resX,
        int resY,
        int mipCount,
        string path,
        JobHandle dependency
    )
        where T : struct
    {
        var pathHandle = new ObjectHandle<string>(path);
        var handle = new SaveDdsJob
        {
            data = data.Reinterpret<byte>(UnsafeUtility.SizeOf<T>()),
            format = format,
            resX = resX,
            resY = resY,
            mipCount = mipCount,
            path = pathHandle,
        }.Schedule(dependency);

        handle = pathHandle.Dispose(handle);
        return data.Dispose(handle);
    }

    /// <summary>
    /// Encodes a pixel array to PNG and writes it to disk.
    /// Runs on a worker thread — not burst-compiled since it calls managed I/O APIs.
    /// </summary>
    struct SavePngJob<T> : IJob
        where T : struct
    {
        [ReadOnly]
        public NativeArray<T> pixels;

        public GraphicsFormat format;

        public int resX,
            resY;

        public ObjectHandle<string> path;

        public void Execute()
        {
            using var png = ImageConversion.EncodeNativeArrayToPNG(
                pixels,
                format,
                (uint)resX,
                (uint)resY
            );
            File.WriteAllBytes(path.Target, png.ToArray());
        }
    }

    enum DdsFormat
    {
        R8,
        R16,
        BC1,
        BC3,
        BC4,
        BC5,
        BC7,
    }

    /// <summary>
    /// Writes already-encoded pixel data, holding every mip level back to back, to disk as a DDS
    /// file. Not burst-compiled since it calls managed I/O APIs.
    /// </summary>
    /// <remarks>
    /// Rows are written in the order they are stored, which is how KSP lays out its own DDS
    /// textures.
    /// </remarks>
    unsafe struct SaveDdsJob : IJob
    {
        [ReadOnly]
        public NativeArray<byte> data;

        public DdsFormat format;

        public int resX;
        public int resY;
        public int mipCount;

        public ObjectHandle<string> path;

        const uint DdsMagic = 0x20534444; // "DDS "
        const uint DdsHeaderSize = 124;
        const uint DdsPixelFormatSize = 32;
        const uint DdsdCaps = 0x1;
        const uint DdsdHeight = 0x2;
        const uint DdsdWidth = 0x4;
        const uint DdsdPitch = 0x8;
        const uint DdsdPixelFormat = 0x1000;
        const uint DdsdMipMapCount = 0x20000;
        const uint DdsdLinearSize = 0x80000;
        const uint DdpfFourCC = 0x4;
        const uint DdpfLuminance = 0x20000;
        const uint DdsCapsComplex = 0x8;
        const uint DdsCapsTexture = 0x1000;
        const uint DdsCapsMipMap = 0x400000;

        const uint DxgiFormatBC7UNorm = 98;
        const uint D3D10ResourceDimensionTexture2D = 3;

        static uint FourCC(string code) =>
            code[0] | (uint)code[1] << 8 | (uint)code[2] << 16 | (uint)code[3] << 24;

        public void Execute()
        {
            using var stream = new FileStream(
                path.Target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1 << 16
            );

            WriteHeader(stream, format, resX, resY, mipCount);

            using var source = new UnmanagedMemoryStream(
                (byte*)data.GetUnsafeReadOnlyPtr(),
                data.Length
            );
            source.CopyTo(stream, 1 << 20);
        }

        public static void WriteHeader(
            Stream stream,
            DdsFormat format,
            int resX,
            int resY,
            int mipCount
        )
        {
            bool compressed = format is not (DdsFormat.R8 or DdsFormat.R16);
            uint pitchOrLinearSize = format switch
            {
                DdsFormat.R8 => (uint)resX,
                DdsFormat.R16 => (uint)resX * 2,
                _ => (uint)MipLevelBytes(format, resX, resY),
            };

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);

            writer.Write(DdsMagic);

            // DDS_HEADER
            writer.Write(DdsHeaderSize);
            writer.Write(
                DdsdCaps
                    | DdsdHeight
                    | DdsdWidth
                    | DdsdPixelFormat
                    | DdsdMipMapCount
                    | (compressed ? DdsdLinearSize : DdsdPitch)
            );
            writer.Write((uint)resY);
            writer.Write((uint)resX);
            writer.Write(pitchOrLinearSize);
            writer.Write(0u); // depth
            writer.Write((uint)mipCount);
            for (int i = 0; i < 11; i++)
                writer.Write(0u); // reserved

            // DDS_PIXELFORMAT
            writer.Write(DdsPixelFormatSize);
            switch (format)
            {
                case DdsFormat.R8:
                    WriteLuminanceFormat(writer, 8, 0xFF);
                    break;

                case DdsFormat.R16:
                    WriteLuminanceFormat(writer, 16, 0xFFFF);
                    break;

                default:
                    var fourCC = format switch
                    {
                        DdsFormat.BC1 => "DXT1",
                        DdsFormat.BC3 => "DXT5",
                        DdsFormat.BC4 => "ATI1",
                        DdsFormat.BC5 => "ATI2",
                        _ => "DX10",
                    };

                    writer.Write(DdpfFourCC);
                    writer.Write(FourCC(fourCC));
                    for (int i = 0; i < 5; i++)
                        writer.Write(0u); // rgbBitCount and masks
                    break;
            }

            writer.Write(DdsCapsTexture | DdsCapsComplex | DdsCapsMipMap);
            writer.Write(0u); // caps2
            writer.Write(0u); // caps3
            writer.Write(0u); // caps4
            writer.Write(0u); // reserved2

            // BC7 has no legacy FourCC, so it needs the DX10 extension header.
            if (format == DdsFormat.BC7)
            {
                writer.Write(DxgiFormatBC7UNorm);
                writer.Write(D3D10ResourceDimensionTexture2D);
                writer.Write(0u); // miscFlag
                writer.Write(1u); // arraySize
                writer.Write(0u); // miscFlags2
            }
        }

        static void WriteLuminanceFormat(BinaryWriter writer, uint bits, uint mask)
        {
            writer.Write(DdpfLuminance);
            writer.Write(0u); // fourCC
            writer.Write(bits);
            writer.Write(mask); // rBitMask
            writer.Write(0u); // gBitMask
            writer.Write(0u); // bBitMask
            writer.Write(0u); // aBitMask
        }
    }
    #endregion

    #region Semaphore
    static Permit ActivePermit = null;
    static readonly Queue<Permit> PermitQueue = [];

    class Permit : CustomYieldInstruction, IDisposable
    {
        bool registered = false;

        public override bool keepWaiting => !TryAcquire();
        public bool IsActive => TryAcquire();

        bool TryAcquire()
        {
            if (ReferenceEquals(ActivePermit, this))
                return true;

            if (ActivePermit is null)
            {
                ActivePermit = this;
                return true;
            }

            if (!registered)
            {
                PermitQueue.Enqueue(this);
                registered = true;
            }

            return false;
        }

        public void Dispose()
        {
            if (!ReferenceEquals(ActivePermit, this))
                return;

            if (PermitQueue.TryDequeue(out var permit))
                ActivePermit = permit;
            else
                ActivePermit = null;
        }
    }

    #endregion

    #region Other Jobs

    [BurstCompile]
    struct ReduceMinMaxJob : IJob
    {
        [ReadOnly]
        public NativeArray<float> blockMinMax;

        [WriteOnly]
        public NativeArray<float> minMax;

        public void Execute()
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;

            for (int i = 0; i < blockMinMax.Length; i += 2)
            {
                min = Math.Min(min, blockMinMax[i]);
                max = Math.Max(max, blockMinMax[i + 1]);
            }

            minMax[0] = min;
            minMax[1] = max;
        }
    }

    #endregion

    #region Utils

    struct ArrayDisposeGuard<T>(T[] values) : IDisposable
        where T : IDisposable
    {
        T[] values = values;

        public void Dispose()
        {
            List<Exception> exceptions = null;

            var values = this.values;
            this.values = [];

            foreach (var value in values)
            {
                try
                {
                    value.Dispose();
                }
                catch (Exception e)
                {
                    exceptions ??= [];
                    exceptions.Add(e);
                }
            }

            if (exceptions is null)
                return;
            if (exceptions.Count == 1)
                throw exceptions[0];

            throw new AggregateException(exceptions);
        }
    }

    #endregion
}
