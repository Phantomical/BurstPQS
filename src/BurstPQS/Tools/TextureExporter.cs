using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BurstPQS.Jobs;
using BurstPQS.Util;
using Contracts.Parameters;
using Steamworks;
using Unity.Collections;
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

internal struct TextureExportOptions
{
    public int width;
    public int height;
    public bool exportHeight;
    public bool exportColor;
    public bool exportNormal;
    public HeightFormat heightFormat;
    public bool orientNorthUp;
}

internal static class TextureExporter
{
    const int BlockSize = 128;

    public static bool IsExporting { get; private set; }
    public static string StatusMessage { get; set; } = "";

    public static IEnumerator ExportPlanet(CelestialBody body, TextureExportOptions options)
    {
        var displayName = body.displayName.LocalizeRemoveGender();
        var pqs = body.pqsController;
        if (pqs == null)
        {
            ScreenMessages.PostScreenMessage(
                $"Body {displayName} has no PQS controller. It will be skipped"
            );
            yield break;
        }

        var batchPQS = pqs.GetComponent<BatchPQS>();
        if (batchPQS == null)
        {
            ScreenMessages.PostScreenMessage(
                $"Skipping {displayName} since it does not have a BatchPQS component"
            );
            Debug.LogWarning(
                $"[BurstPQS] Skipping {body.name} since it does not have a BatchPQS component"
            );
            yield break;
        }

        if (batchPQS.Fallback)
        {
            ScreenMessages.PostScreenMessage(
                $"Skipping {displayName} since it does not have a BatchPQS componen"
            );
            Debug.LogWarning(
                $"[BurstPQS] Skipping {body.name} since it does not have a BatchPQS component"
            );
            yield break;
        }

        using var exporter = new PlanetExporter(body, options);
        yield return exporter.ExecuteComputeJobs();
        yield return exporter.ComputeMinMax();
        yield return exporter.ExportTextures();
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

    #region Exporter
    class PlanetExporter(CelestialBody body, TextureExportOptions options) : IDisposable
    {
        readonly PQS pqs = body.pqsController;
        readonly BatchPQS batchPQS = body.pqsController.GetComponent<BatchPQS>();
        readonly CelestialBody body = body;
        readonly TextureExportOptions options = options;
        readonly string bodyName = body.bodyName.LocalizeRemoveGender();

        bool hasOcean;
        Permit permit;

        NativeArray<float> heights;
        NativeArray<Color32> normals;
        NativeArray<Color32> colors;
        NativeArray<float> minmax;

        NativeArray<float> oceanHeights;
        NativeArray<Color32> oceanNormals;
        NativeArray<Color32> oceanColors;
        NativeArray<float> oceanMinMax;

        public IEnumerator ExecuteComputeJobs()
        {
            permit = new Permit();
            if (!permit.IsActive)
                yield return permit;

            int resX = options.width;
            int resY = options.height;
            int pixelCount = resX * resY;
            int numBlocksX = (resX + BlockSize - 1) / BlockSize;
            int numBlocksY = (resY + BlockSize - 1) / BlockSize;
            int totalBlocks = numBlocksX * numBlocksY;

            // Create a PQ under a disabled GameObject for OnQuadPreBuild.
            using var goGuard = new GameObjectGuard(new GameObject("BurstPQS_TextureExportQuad"));
            goGuard.GameObject.SetActive(false);
            var quad = goGuard.AddPQ(pqs);

            heights = new NativeArray<float>(
                pixelCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
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

            // Ocean sphere (first child of the main PQS, if any).
            hasOcean = body.ocean && pqs.ChildSpheres is { Length: > 0 };
            PQS oceanPQS = hasOcean ? pqs.ChildSpheres[0] : null;
            BatchPQS oceanBatchPQS = oceanPQS?.GetComponent<BatchPQS>();
            hasOcean = hasOcean && oceanBatchPQS != null;
            bool oceanFallback = hasOcean && oceanBatchPQS.Fallback;

            using var oceanGoGuard =
                hasOcean && !oceanFallback
                    ? new GameObjectGuard(new GameObject("BurstPQS_TextureExportOceanQuad"))
                    : default;
            PQ oceanQuad = null;
            if (hasOcean && !oceanFallback)
            {
                oceanGoGuard.GameObject.SetActive(false);
                oceanQuad = oceanGoGuard.AddPQ(oceanPQS);
            }

            if (hasOcean)
            {
                oceanHeights = new NativeArray<float>(
                    pixelCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
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
            int workerCount = JobsUtility.JobWorkerCount;
            var blocks = new Queue<BlockState>();
            int complete = 0;

            var lastYield = Time.realtimeSinceStartup;

            for (int by = 0; by < numBlocksY; by++)
            {
                for (int bx = 0; bx < numBlocksX; bx++)
                {
                    int startX = bx * BlockSize;
                    int startY = by * BlockSize;
                    int blockW = Math.Min(BlockSize, resX - startX);
                    int blockH = Math.Min(BlockSize, resY - startY);
                    int blockSize = blockW * blockH;

                    // Create a fresh job set for this block so OnQuadPreBuild
                    // is called per-block, allowing mods to set up per-quad state.
                    var block = new BlockState { jobSetGuard = new JobSetGuard(batchPQS, quad) };

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

                    JobHandle handle = new TextureExportBlockJob
                    {
                        jobSet = block.jobSetGuard.Handle,
                        sphere = sphere,
                        resX = resX,
                        resY = resY,
                        startX = startX,
                        startY = startY,
                        blockW = blockW,
                        blockH = blockH,
                        blockHeights = blockHeights,
                        blockNormals = blockNormals,
                        blockColors = blockColors,
                    }.Schedule();

                    // Copy terrain data to terrain output arrays.
                    var terrainCopyH = new TextureExportCopyHeightsJob
                    {
                        blockHeights = blockHeights,
                        outputHeights = heights,
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
                    }.Schedule(terrainCopyH);

                    var terrainCopyC = new TextureExportCopyColorsJob
                    {
                        blockColors = blockColors,
                        outputColors = colors,
                        resX = resX,
                        startX = startX,
                        startY = startY,
                        blockW = blockW,
                        blockH = blockH,
                    }.Schedule(terrainCopyN);

                    handle = JobHandle.CombineDependencies(
                        terrainCopyH,
                        terrainCopyH,
                        terrainCopyC
                    );

                    if (hasOcean)
                    {
                        block.oceanJobSetGuard = oceanFallback
                            ? JobSetGuard.CreateEmpty()
                            : new JobSetGuard(oceanBatchPQS, oceanQuad);

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

                        handle = new TextureExportOceanBlockJob
                        {
                            jobSet = block.oceanJobSetGuard.Handle,
                            sphere = oceanSphere,
                            resX = resX,
                            resY = resY,
                            startX = startX,
                            startY = startY,
                            blockW = blockW,
                            blockH = blockH,
                            blockHeights = oceanBlockHeights,
                            blockNormals = oceanBlockNormals,
                        }.Schedule(handle);

                        handle = new TextureExportBlendOceanJob
                        {
                            blockHeights = blockHeights,
                            blockNormals = blockNormals,
                            blockColors = blockColors,
                            oceanHeights = oceanBlockHeights,
                            oceanNormals = oceanBlockNormals,
                            oceanColor = oceanColor,
                        }.Schedule(handle);

                        // Copy blended data to ocean output arrays.
                        var oceanCopyH = new TextureExportCopyHeightsJob
                        {
                            blockHeights = blockHeights,
                            outputHeights = oceanHeights,
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

                        oceanBlockHeights.Dispose(handle);
                        oceanBlockNormals.Dispose(handle);
                    }

                    blockHeights.Dispose(handle);
                    blockNormals.Dispose(handle);
                    blockColors.Dispose(handle);

                    block.handle = handle;
                    block.scheduledFrame = Time.frameCount;
                    blocks.Enqueue(block);

                    bool yielded = false;

                    // Flush and drain completed blocks. Force-complete any block
                    // that has been in-flight for 4+ frames to avoid interfering
                    // with other jobs (e.g. TempJob dispose jobs).
                    if (blocks.Count >= maxInFlight)
                    {
                        JobHandle.ScheduleBatchedJobs();

                        while (blocks.TryPeek(out block))
                        {
                            if (!block.IsCompleted)
                            {
                                if (blocks.Count >= maxInFlight)
                                {
                                    StatusMessage =
                                        $"Exporting {bodyName}: computed {complete}/{totalBlocks} chunks";

                                    while (!block.IsCompleted && block.HasSpareTime)
                                    {
                                        yielded = true;
                                        yield return null;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }

                            if (!block.IsCompleted && !block.HasSpareTime)
                                maxInFlight = Math.Max(
                                    JobsUtility.JobWorkerCount,
                                    maxInFlight - maxInFlight / 4
                                );
                            else
                                maxInFlight += 1;

                            blocks.Dequeue();
                            block.Dispose();
                            complete++;
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
                }

                // Flush remaining scheduled jobs and drain the queue.
                JobHandle.ScheduleBatchedJobs();
                while (blocks.TryDequeue(out var block))
                {
                    if (!block.handle.IsCompleted)
                    {
                        StatusMessage =
                            $"Exporting {bodyName}: computed {complete}/{totalBlocks} chunks";

                        while (!block.IsCompleted && block.HasSpareTime)
                            yield return null;
                    }

                    block.Dispose();
                    complete++;
                }

                StatusMessage = $"Exporting {bodyName}: computed {complete}/{totalBlocks} chunks";
            }
        }

        public IEnumerator ComputeMinMax()
        {
            var pqsMin = (float)(pqs.radiusMin - pqs.radius);
            var pqsMax = (float)(pqs.radiusMax - pqs.radius);

            JobHandle handle = new TextureExportClampJob
            {
                values = heights,
                min = pqsMin,
                max = pqsMax,
            }.ScheduleBatch(heights.Length, 8192);

            minmax = new(2, Allocator.Persistent);
            handle = TextureHeightMinMaxNarrowJob.Schedule(heights, minmax, handle);

            if (hasOcean)
            {
                var oceanPQS = pqs.ChildSpheres[0];
                var oceanMin = (float)(oceanPQS.radiusMin - pqs.radius);
                var oceanMax = (float)(oceanPQS.radiusMax - pqs.radius);

                var oceanHandle = new TextureExportClampJob
                {
                    values = oceanHeights,
                    min = Math.Max(pqsMin, oceanMin),
                    max = Math.Max(pqsMax, oceanMax),
                }.ScheduleBatch(oceanHeights.Length, 8192);

                oceanMinMax = new(2, Allocator.Persistent);
                oceanHandle = TextureHeightMinMaxNarrowJob.Schedule(
                    oceanHeights,
                    oceanMinMax,
                    oceanHandle
                );

                handle = JobHandle.CombineDependencies(handle, oceanHandle);
            }

            JobHandle.ScheduleBatchedJobs();

            int start = Time.frameCount;

            StatusMessage = $"Exporting {bodyName}: computed min/max heights";
            while (!handle.IsCompleted && start + 4 < Time.frameCount)
                yield return null;

            handle.Complete();
        }

        public IEnumerator ExportTextures()
        {
            float minH = minmax[0];
            float maxH = minmax[1];
            float oceanMinH = hasOcean ? oceanMinMax[0] : 0;
            float oceanMaxH = hasOcean ? oceanMinMax[1] : 0;

            minmax.Dispose();
            minmax = default;

            if (hasOcean)
            {
                oceanMinMax.Dispose();
                oceanMinMax = default;
            }

            Debug.Log(
                $"[BurstPQS] {body.name}: min altitude = {minH:F1}m, max altitude = {maxH:F1}m"
            );
            if (hasOcean)
                Debug.Log(
                    $"[BurstPQS] {body.name}: ocean min = {oceanMinH:F1}m, ocean max = {oceanMaxH:F1}m"
                );

            JobHandle handle = default;
            int resX = options.width;
            int resY = options.height;
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
            string exportDir = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "PluginData",
                "BurstPQS",
                body.name
            );
            if (!Directory.Exists(exportDir))
                Directory.CreateDirectory(exportDir);

            if (options.exportNormal)
            {
                var normalHandle = SaveColors(
                    ref normals,
                    exportDir,
                    $"{body.name}_Normal",
                    handle
                );
                handles.Enqueue(normalHandle);

                if (hasOcean)
                {
                    var oceanNormalHandle = SaveColors(
                        ref oceanNormals,
                        exportDir,
                        $"{body.name}_Normal_Ocean",
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
                var colorHandle = SaveColors(ref colors, exportDir, $"{body.name}_Color", handle);
                handles.Enqueue(colorHandle);

                if (hasOcean)
                {
                    var oceanColorHandle = SaveColors(
                        ref oceanColors,
                        exportDir,
                        $"{body.name}_Color_Ocean",
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

            JobHandle.ScheduleBatchedJobs();

            if (options.exportHeight)
            {
                var heightHandle = SaveHeights(
                    ref heights,
                    minH,
                    maxH,
                    options.heightFormat,
                    exportDir,
                    $"{body.name}_Height",
                    handle
                );
                handles.Enqueue(heightHandle);

                if (hasOcean)
                {
                    var oceanHeightHandle = SaveHeights(
                        ref oceanHeights,
                        oceanMinH,
                        oceanMaxH,
                        options.heightFormat,
                        exportDir,
                        $"{body.name}_Height_Ocean",
                        handle
                    );
                    handles.Enqueue(oceanHeightHandle);
                }
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

        JobHandle SaveHeights(
            ref NativeArray<float> heights,
            float minH,
            float maxH,
            HeightFormat format,
            string exportDir,
            string baseName,
            JobHandle dependsOn = default
        )
        {
            int resX = options.width;
            int resY = options.height;
            int pixelCount = resX * resY;
            string path;
            JobHandle handle;

            if (format == HeightFormat.R16)
            {
                var pixels = new NativeArray<ushort>(
                    pixelCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
                var encodeHandle = new TextureExportEncodeHeightsR16Job
                {
                    heights = heights,
                    output = pixels,
                    minH = minH,
                    maxH = maxH,
                }.ScheduleBatch(heights.Length, 8192, dependsOn);
                heights.Dispose(encodeHandle);

                path = Path.Combine(exportDir, baseName + ".dds");
                handle = ScheduleSaveDdsR16Job(pixels, resX, resY, path, encodeHandle);
                pixels.Dispose(handle);
            }
            else
            {
                var pixels = new NativeArray<Color32>(
                    pixelCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
                var encodeHandle = new TextureExportEncodeHeightsRGB24Job
                {
                    heights = heights,
                    output = pixels,
                    minH = minH,
                    maxH = maxH,
                }.ScheduleBatch(heights.Length, 8192, dependsOn);
                heights.Dispose(encodeHandle);

                path = Path.Combine(exportDir, baseName + ".png");
                handle = ScheduleSaveJob(pixels, resX, resY, path, encodeHandle);
                pixels.Dispose(handle);
            }

            Debug.Log($"[BurstPQS] Writing {path}");
            return handle;
        }

        JobHandle SaveColors(
            ref NativeArray<Color32> source,
            string exportDir,
            string baseName,
            JobHandle dependsOn = default
        )
        {
            int resX = options.width;
            int resY = options.height;

            var path = Path.Combine(exportDir, baseName + ".png");
            Debug.Log($"[BurstPQS] Writing {path}");

            var handle = ScheduleSaveJob(source, resX, resY, path, dependsOn);
            source.Dispose(handle);
            return handle;
        }

        public void Dispose()
        {
            permit?.Dispose();

            minmax.Dispose();
            oceanMinMax.Dispose();

            heights.Dispose();
            normals.Dispose();
            colors.Dispose();
            minmax.Dispose();

            oceanHeights.Dispose();
            oceanNormals.Dispose();
            oceanColors.Dispose();
            oceanMinMax.Dispose();
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
        pathHandle.Dispose(handle);
        return handle;
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

        pathHandle.Dispose(handle);
        return handle;
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

        public int resX,
            resY;

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
}
