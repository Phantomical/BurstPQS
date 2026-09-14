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
    RGB24,
    R16,
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
    public bool orientNorthUp;

    public readonly int Width => resolution;

    /// <summary>
    /// An equirectangular map covers twice as much longitude as latitude, so it is half as
    /// tall as it is wide. A cube face is square.
    /// </summary>
    public readonly int Height =>
        projection == TextureProjection.Equirectangular ? resolution / 2 : resolution;
}

internal static class TextureExporter
{
    const int BlockSize = 128;

    public static bool IsExporting { get; private set; }
    public static string StatusMessage { get; set; } = "";

    private static Coroutine StartCoroutine(IEnumerator coroutine) =>
        TextureExportRunner.Instance.StartCoroutine(coroutine);

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
        var coroutines = new List<Coroutine>(exporters.Length);

        foreach (var exporter in exporters)
            coroutines.Add(StartCoroutine(exporter.Export()));

        foreach (var coro in coroutines)
            yield return coro;

        if (options.exportHeight)
        {
            StatusMessage = $"Exporting {displayName}: saving height maps";
            var coro1 = StartCoroutine(heightMaps.Save(options.heightFormat));
            var coro2 = StartCoroutine(oceanHeightMaps.Save(options.heightFormat));

            yield return coro1;
            yield return coro2;
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
                string path;
                JobHandle handle;

                if (format == HeightFormat.R16)
                {
                    var pixels = new NativeArray<ushort>(
                        Heights.Length,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory
                    );
                    var encodeHandle = new TextureExportEncodeHeightsR16Job
                    {
                        heights = Heights,
                        output = pixels,
                        minH = minH,
                        maxH = maxH,
                    }.ScheduleBatch(Heights.Length, 8192);

                    path = outputPath + ".dds";
                    handle = ScheduleSaveDdsR16Job(
                        pixels,
                        resX,
                        resY,
                        path,
                        ScheduleRelease(encodeHandle)
                    );
                    handle = pixels.Dispose(handle);
                }
                else
                {
                    var pixels = new NativeArray<Color32>(
                        Heights.Length,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory
                    );
                    var encodeHandle = new TextureExportEncodeHeightsRGB24Job
                    {
                        heights = Heights,
                        output = pixels,
                        minH = minH,
                        maxH = maxH,
                    }.ScheduleBatch(Heights.Length, 8192);

                    path = outputPath + ".png";
                    handle = ScheduleSaveJob(
                        pixels,
                        resX,
                        resY,
                        path,
                        ScheduleRelease(encodeHandle)
                    );
                    handle = pixels.Dispose(handle);
                }

                Debug.Log($"[BurstPQS] Writing {path}");
                return handle;
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
                var normalHandle = SaveColors(
                    ref normals,
                    exportDir,
                    $"{body.name}_Normal{suffix}",
                    handle
                );
                handles.Enqueue(normalHandle);

                if (hasOcean)
                {
                    var oceanNormalHandle = SaveColors(
                        ref oceanNormals,
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
                var colorHandle = SaveColors(
                    ref colors,
                    exportDir,
                    $"{body.name}_Color{suffix}",
                    handle
                );
                handles.Enqueue(colorHandle);

                if (hasOcean)
                {
                    var oceanColorHandle = SaveColors(
                        ref oceanColors,
                        exportDir,
                        $"{body.name}_Color_Ocean{suffix}",
                        handle
                    );
                    handles.Enqueue(oceanColorHandle);
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
                handle = blockJob.Schedule();
            }
            else
            {
                using (new FallbackBuildingMapsGuard(pqs))
                    blockJob.ExecuteFallback(pqs, quad);

                handle = default;
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

                if (oceanBatchPQS is null)
                {
                    handle = oceanJob.Schedule(handle);
                }
                else
                {
                    using (new FallbackBuildingMapsGuard(oceanPQS))
                        oceanJob.ExecuteFallback(oceanPQS, oceanQuad);
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

        JobHandle SaveColors(
            ref NativeArray<Color32> source,
            string exportDir,
            string baseName,
            JobHandle dependsOn = default
        )
        {
            int resX = options.Width;
            int resY = options.Height;

            var path = Path.Combine(exportDir, baseName + ".png");
            Debug.Log($"[BurstPQS] Writing {path}");

            var handle = ScheduleSaveJob(source, resX, resY, path, dependsOn);
            return source.Dispose(handle);
        }

        public void Dispose()
        {
            permit?.Dispose();

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
    static JobHandle ScheduleSaveJob(
        NativeArray<Color32> pixels,
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

        return pathHandle.Dispose(handle);
    }

    static JobHandle ScheduleSaveDdsR16Job(
        NativeArray<ushort> pixels,
        int resX,
        int resY,
        string path,
        JobHandle dependency
    )
    {
        var pathHandle = new ObjectHandle<string>(path);
        var handle = new SaveDdsR16Job
        {
            pixels = pixels,
            resX = resX,
            resY = resY,
            path = pathHandle,
        }.Schedule(dependency);

        return pathHandle.Dispose(handle);
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

    /// <summary>
    /// Writes R16 height data as an uncompressed DDS file.
    /// DDS is used because PNG does not support single-channel 16-bit.
    /// </summary>
    struct SaveDdsR16Job : IJob
    {
        [ReadOnly]
        public NativeArray<ushort> pixels;

        public int resX;
        public int resY;

        public ObjectHandle<string> path;

        // DDS constants
        const uint DdsMagic = 0x20534444; // "DDS "
        const uint DdsHeaderSize = 124;
        const uint DdsPixelFormatSize = 32;
        const uint DdsfCaps = 0x1;
        const uint DdsfHeight = 0x2;
        const uint DdsfWidth = 0x4;
        const uint DdsfPixelFormat = 0x1000;
        const uint DdsfLinearSize = 0x80000;
        const uint DdpfLuminance = 0x20000;
        const uint DdsCapsTexture = 0x1000;

        public void Execute()
        {
            int dataSize = resX * resY * 2;
            int headerSize = 4 + (int)DdsHeaderSize; // magic + header
            var buffer = new byte[headerSize + dataSize];

            using (var stream = new MemoryStream(buffer))
            using (var writer = new BinaryWriter(stream))
            {
                // Magic
                writer.Write(DdsMagic);

                // DDS_HEADER
                writer.Write(DdsHeaderSize);
                writer.Write(DdsfCaps | DdsfHeight | DdsfWidth | DdsfPixelFormat | DdsfLinearSize);
                writer.Write((uint)resY); // height
                writer.Write((uint)resX); // width
                writer.Write((uint)dataSize); // pitchOrLinearSize
                writer.Write(0u); // depth
                writer.Write(0u); // mipMapCount
                for (int i = 0; i < 11; i++)
                    writer.Write(0u); // reserved

                // DDS_PIXELFORMAT
                writer.Write(DdsPixelFormatSize);
                writer.Write(DdpfLuminance);
                writer.Write(0u); // fourCC
                writer.Write(16u); // rgbBitCount
                writer.Write(0x0000FFFFu); // rBitMask
                writer.Write(0u); // gBitMask
                writer.Write(0u); // bBitMask
                writer.Write(0u); // aBitMask

                // Caps
                writer.Write(DdsCapsTexture);
                writer.Write(0u); // caps2
                writer.Write(0u); // caps3
                writer.Write(0u); // caps4
                writer.Write(0u); // reserved2

                // Pixel data
                for (int i = 0; i < pixels.Length; i++)
                    writer.Write(pixels[i]);
            }

            File.WriteAllBytes(path.Target, buffer);
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
