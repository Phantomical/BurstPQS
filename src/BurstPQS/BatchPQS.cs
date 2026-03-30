using System;
using System.Collections.Generic;
using System.Text;
using BurstPQS.Jobs;
using BurstPQS.Patches;
using BurstPQS.Util;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace BurstPQS;

public class BatchPQS : MonoBehaviour
{
    internal static bool ForceFallback = false;
    static readonly ProfilerMarker BuildQuadMarker = new("BatchPQS.BuildQuad");

    private PQS pqs;
    private BatchPQSMod[] mods;

    // Are there unsupported mods and do we need to fall back to the stock
    // implementation?
    private bool _fallback = false;
    internal bool Fallback
    {
        get => _fallback || ForceFallback;
        set => _fallback = value;
    }
    internal string FallbackMessage { get; private set; }

    private readonly Dictionary<PQ, PendingBuild> pending = [];
    private readonly Queue<PQ> buildQueue = [];
    private NativeList<MeshDataStruct> disposeList;

    void Awake()
    {
        pqs = GetComponent<PQS>();
    }

    void OnDestroy()
    {
        foreach (var mod in mods)
        {
            try
            {
                mod.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    public bool BuildQuad(PQ quad)
    {
        using var scope = BuildQuadMarker.Auto();

        if (pending.TryGetValue(quad, out var build))
            pending.Remove(quad);
        else if (Fallback)
            return PQS_RevPatch.BuildQuad(pqs, quad);
        else
        {
            build = new(this, quad);

            if (!build.StartBuild())
                return false;
        }

        using (build)
            build.Complete();
        return true;
    }

    #region UpdateQuads
    static readonly ProfilerMarker UpdateQuadsMarker = new("UpdateQuads");
    static readonly ProfilerMarker UpdateTargetRelativityMarker = new("UpdateTargetRelativity");
    static readonly ProfilerMarker UpdateSubdivisionMarker = new("UpdateSubdivision");
    static readonly ProfilerMarker CompleteQueuedBuildsMarker = new("CompleteQueuedBuilds");
    static readonly ProfilerMarker UpdateEdgesMarker = new("UpdateEdges");

    public void UpdateQuads()
    {
        if (pqs.quads == null)
            return;

        using var scope = UpdateQuadsMarker.Auto();

        pqs.isThinking = true;

        SortQuadsByDistance(pqs.quads, pqs.relativeTargetPosition);

        var subdivisionUpdate = new SubdivisionUpdate(pqs, activeQuads);
        subdivisionUpdate.ScheduleJobs();
        subdivisionUpdate.Complete();

        JobHandle.ScheduleBatchedJobs();

        CompleteQueuedBuilds();
        JobHandle.ScheduleBatchedJobs();

        if (pqs.reqCustomNormals)
        {
            QueueEdgeBuilds();
            using (UpdateEdgesMarker.Auto())
                pqs.UpdateEdges();
        }

        pqs.isThinking = false;
    }

    readonly struct BatchDisposeScope : IDisposable
    {
        readonly BatchPQS batchPQS;

        public BatchDisposeScope(BatchPQS batchPQS, int capacity)
        {
            this.batchPQS = batchPQS;
            batchPQS.disposeList = new NativeList<MeshDataStruct>(capacity, Allocator.TempJob);
        }

        public void Dispose()
        {
            var list = batchPQS.disposeList;
            batchPQS.disposeList = default;
            if (list.Length == 0)
                list.Dispose();
            else
                new MeshDataStruct.BatchDisposeJob(list).Schedule();
        }
    }

    void CompleteQueuedBuilds()
    {
        using var scope = CompleteQueuedBuildsMarker.Auto();
        using var disposeScope = new BatchDisposeScope(this, buildQueue.Count);

        while (buildQueue.TryDequeue(out var quad))
        {
            if (!pending.TryGetValue(quad, out var build))
                continue;

            pending.Remove(quad);

            try
            {
                build.Complete();
                quad.isBuilt = true;

                // Apply correct edge stitching immediately after build.
                // PendingBuild.Complete() always builds with cacheIndices[0] (no stitching)
                // and resets edgeState to Reset. Stock code fixes this every frame via the
                // recursive UpdateSubdivision walk, but we use selective UpdateVisibility,
                // so quads would keep the unstitched triangles until their visibility flips.
                var newEdgeState = quad.GetEdgeState();
                if (newEdgeState != quad.edgeState)
                {
                    quad.mesh.triangles = PQS.cacheIndices[(int)newEdgeState];
                    quad.edgeState = newEdgeState;
                }

                quad.QueueForNormalUpdate();
            }
            finally
            {
                build.Dispose();
            }
        }
    }

    static readonly ProfilerMarker QueueEdgeBuildsMarker = new("QueueEdgeBuilds");

    void QueueEdgeBuilds()
    {
        if (Fallback)
            return;

        using var scope = QueueEdgeBuildsMarker.Auto();

        var list = pqs.normalUpdateList;
        for (int i = 0; i < list.Count; i++)
        {
            var q = list[i];
            if (q == null || q.isSubdivided || !q.isVisible)
                continue;
            if (!q.isActive || !q.isBuilt)
                continue;
            if (q.parent.IsNullOrDestroyed() || !q.parent.isSubdivided)
                continue;

            QueueNeighborBuild(q, q.north);
            QueueNeighborBuild(q, q.south);
            QueueNeighborBuild(q, q.east);
            QueueNeighborBuild(q, q.west);

            QueueCornerBuild(q, q.north);
            QueueCornerBuild(q, q.west);
            QueueCornerBuild(q, q.south);
            QueueCornerBuild(q, q.east);
        }

        JobHandle.ScheduleBatchedJobs();
    }

    void QueueNeighborBuild(PQ q, PQ neighbor)
    {
        if (neighbor.subdivision == q.subdivision)
        {
            if (neighbor.isSubdivided)
            {
                neighbor.GetEdgeQuads(q, out var left, out var right);
                if (left.IsNotNullOrDestroyed())
                    BuildDeferred(left);
                if (right.IsNotNullOrDestroyed())
                    BuildDeferred(right);
            }
            else
            {
                BuildDeferred(neighbor);
            }
        }
        else if (neighbor.subdivision < q.subdivision)
        {
            BuildDeferred(neighbor);
        }
    }

    void QueueCornerBuild(PQ q, PQ neighbor)
    {
        // GetRightmostCornerPQ internally calls BuildDeferred (transpiled)
        // on intermediate quads. We also BuildDeferred the result to cover
        // the Build() call in GetRightmostCornerNormal.
        var cornerPQ = q.GetRightmostCornerPQ(neighbor);
        if (cornerPQ.IsNotNullOrDestroyed())
            BuildDeferred(cornerPQ);
    }

    internal void ClearActiveQuads() => activeQuads.Clear();

    #region SubdivisionUpdate
    private readonly List<PQ> activeQuads = new(2048);

    struct SubdivisionUpdate(PQS pqs, List<PQ> activeQuads)
    {
        static readonly ProfilerMarker CollectActiveQuadsMarker = new("CollectActiveQuads");

        readonly PQS pqs = pqs;
        readonly List<PQ> activeQuads = activeQuads;

        NativeArray<QuadSnapshot> snapshots;
        NativeArray<QuadResult> results;
        NativeArray<SubdivisionAction> actions;
        NativeList<int> subdivideIndices;
        NativeList<int> collapseIndices;
        NativeList<int> onUpdateIndices;
        NativeQueue<int> visibilityChangedQueue;
        JobHandle subdivideHandle;
        JobHandle collapseHandle;
        JobHandle scatterHandle;
        JobHandle onUpdateHandle;

        public void ScheduleJobs()
        {
            using var scope = UpdateTargetRelativityMarker.Auto();

            if (activeQuads.Count == 0)
                CollectActiveQuads();

            int count = activeQuads.Count;
            if (count == 0)
                return;

            snapshots = new NativeArray<QuadSnapshot>(count, Allocator.TempJob);

            var quadsHandle = new ObjectHandle<List<PQ>>(activeQuads);

            // Job 1: Gather managed PQ fields into NativeArray<QuadSnapshot>
            var gatherHandle = new GatherQuadDataJob
            {
                quads = quadsHandle,
                snapshots = snapshots,
            }.ScheduleBatch(count, 32);
            JobHandle.ScheduleBatchedJobs();

            quadsHandle.Dispose(gatherHandle);

            results = new NativeArray<QuadResult>(count, Allocator.TempJob);
            actions = new NativeArray<SubdivisionAction>(count, Allocator.TempJob);
            onUpdateIndices = new NativeList<int>(64, Allocator.TempJob);
            visibilityChangedQueue = new NativeQueue<int>(Allocator.TempJob);

            // Copy threshold arrays into NativeArrays for Burst access
            var subdivThresholds = new NativeArray<double>(
                pqs.subdivisionThresholds,
                Allocator.TempJob
            );
            var collapseThresholds = new NativeArray<double>(
                pqs.collapseThresholds,
                Allocator.TempJob
            );

            // Job 2: Burst-compiled computation of gcd1, gcDist, actions, visibility
            var computeHandle = new ComputeSubdivisionJob
            {
                relativeTargetPositionNormalized = BurstUtil.ConvertVector(
                    pqs.relativeTargetPositionNormalized
                ),
                radius = pqs.radius,
                absTargetHeight = Math.Abs(pqs.targetHeight),
                subdivisionThresholds = subdivThresholds,
                collapseThresholds = collapseThresholds,
                maxLevel = pqs.maxLevel,
                minLevel = pqs.minLevel,
                maxLevelAtCurrentTgtSpeed = pqs.maxLevelAtCurrentTgtSpeed,
                visibleRadius = pqs.visibleRadius,
                snapshots = snapshots,
                actions = actions,
                results = results,
                visibilityChangedQueue = visibilityChangedQueue.AsParallelWriter(),
            }.ScheduleBatch(count, 128, gatherHandle);

            // Job 3: Scatter gcd1/gcDist back to managed PQ objects
            var scatterQuadsHandle = new ObjectHandle<List<PQ>>(activeQuads);
            scatterHandle = new ScatterQuadResultsJob
            {
                quads = scatterQuadsHandle,
                results = results,
            }.ScheduleBatch(activeQuads.Count, 32, computeHandle);

            // Job 4: Collect indices of quads with onUpdate delegates (Burst, parallel with scatter)
            onUpdateHandle = new CollectOnUpdateJob
            {
                snapshots = snapshots,
                onUpdateIndices = onUpdateIndices,
            }.Schedule(gatherHandle);

            // Job 5-6: Collect subdivide/collapse indices (Burst, parallel with scatter/onUpdate)
            subdivideIndices = new NativeList<int>(64, Allocator.TempJob);
            collapseIndices = new NativeList<int>(64, Allocator.TempJob);

            subdivideHandle = new CollectActionsJob
            {
                actions = actions,
                target = SubdivisionAction.Subdivide,
                indices = subdivideIndices,
            }.Schedule(computeHandle);
            collapseHandle = new CollectActionsJob
            {
                actions = actions,
                target = SubdivisionAction.Collapse,
                indices = collapseIndices,
            }.Schedule(computeHandle);

            scatterQuadsHandle.Dispose(scatterHandle);
            subdivThresholds.Dispose(computeHandle);
            collapseThresholds.Dispose(computeHandle);
            JobHandle.ScheduleBatchedJobs();
        }

        public void Complete()
        {
            using var scope = UpdateSubdivisionMarker.Auto();
            using var snapshotsGuard = snapshots;
            using var resultsGuard = results;
            using var actionsGuard = actions;
            using var subdivideGuard = subdivideIndices;
            using var collapseGuard = collapseIndices;
            using var onUpdateGuard = onUpdateIndices;
            using var visibilityGuard = visibilityChangedQueue;

            subdivideHandle.Complete();
            // Subdivide closest-first (ascending index order from DFS collection)
            for (int i = 0; i < subdivideIndices.Length; i++)
                activeQuads[subdivideIndices[i]].Subdivide();

            collapseHandle.Complete();
            // Collapse farthest-first (reverse order for bottom-up)
            for (int i = collapseIndices.Length - 1; i >= 0; i--)
                activeQuads[collapseIndices[i]].Collapse();

            bool modified = subdivideIndices.Length != 0 || collapseIndices.Length != 0;
            if (modified)
            {
                // Tree structure changed — edge states may have changed for neighbors,
                // so we must call UpdateVisibility on all leaf quads to fix T-junctions.
                for (int i = 0; i < activeQuads.Count; i++)
                {
                    var q = activeQuads[i];
                    if (q.IsNotNullOrDestroyed() && q.isActive && !q.isSubdivided)
                        q.UpdateVisibility();
                }
            }
            else
            {
                // No tree changes — only update quads whose visibility actually flipped.
                // Edge stitching is stable since no subdivision levels changed.
                while (visibilityChangedQueue.TryDequeue(out int idx))
                {
                    var q = activeQuads[idx];
                    if (q.IsNotNullOrDestroyed() && q.isActive && !q.isSubdivided)
                        q.UpdateVisibility();
                }
            }

            // Fire onUpdate delegates
            onUpdateHandle.Complete();
            for (int i = 0; i < onUpdateIndices.Length; i++)
            {
                var q = activeQuads[onUpdateIndices[i]];
                if (q.IsNotNullOrDestroyed() && q.isActive)
                    q.onUpdate?.Invoke(q);
            }

            // Ensure scatter job has finished writing gcd1/gcDist before returning
            scatterHandle.Complete();

            // Force re-collection next frame if the tree changed
            if (modified)
                activeQuads.Clear();
        }

        void CollectActiveQuads()
        {
            using var scope = CollectActiveQuadsMarker.Auto();

            foreach (var quad in pqs.quads)
            {
                if (quad.IsNullOrDestroyed() || !quad.isActive)
                    continue;

                activeQuads.Add(quad);
            }

            for (int i = 0; i < activeQuads.Count; ++i)
            {
                var quad = activeQuads[i];

                if (!quad.isSubdivided)
                    continue;

                foreach (var subnode in quad.subNodes)
                {
                    if (subnode.IsNullOrDestroyed() || !subnode.isActive)
                        continue;

                    activeQuads.Add(subnode);
                }
            }
        }
    }
    #endregion

    static void SortQuadsByDistance(PQ[] quads, Vector3d relativeTargetPosition)
    {
        int num = quads.Length;
        for (int i = 1; i < num; i++)
        {
            PQ pQ = quads[i];
            int j = i - 1;
            while (
                j >= 0
                && (relativeTargetPosition - quads[j].positionPlanetRelative).sqrMagnitude
                    > (relativeTargetPosition - pQ.positionPlanetRelative).sqrMagnitude
            )
            {
                quads[j + 1] = quads[j];
                j--;
            }
            quads[j + 1] = pQ;
        }
    }
    #endregion

    internal void BuildDeferred(PQ quad)
    {
        if (quad.isBuilt || !quad.isActive || !pqs.quadAllowBuild || quad.isCached)
            return;

        if (Fallback)
        {
            quad.Build();
            return;
        }

        if (quad.isSubdivided)
        {
            foreach (var subnode in quad.subNodes)
                BuildDeferred(subnode);
        }
        else
        {
            if (pending.ContainsKey(quad))
                return;

            var build = new PendingBuild(this, quad);
            if (!build.StartBuild())
                return;

            buildQueue.Enqueue(quad);
            pending.Add(quad, build);
            JobHandle.ScheduleBatchedJobs();
        }
    }

    static readonly ProfilerMarker OnQuadSubdividedMarker = new("BatchPQS.OnQuadSubdivided");

    public void OnQuadSubdivided(PQ quad)
    {
        using var scope = OnQuadSubdividedMarker.Auto();

        if (!Fallback && pqs.quadAllowBuild)
        {
            foreach (var child in quad.subNodes)
            {
                if (child.isSubdivided)
                    continue;
                if (child.gcd1 >= pqs.visibleRadius)
                    continue;

                var build = new PendingBuild(this, child);
                if (!build.StartBuild())
                    continue;

                pending.Add(child, build);
            }
        }

        foreach (var child in quad.subNodes)
            child.UpdateVisibility();
    }

    public void OnQuadDestroy(PQ quad)
    {
        if (!pending.TryGetValue(quad, out var build))
            return;

        using var guard = build;
        pending.Remove(quad);
    }

    #region Method Injections
    internal void PostSetupMods()
    {
        var fallbackMessage = new StringBuilder();
        Fallback = false;

        List<BatchPQSMod> batchMods = new(pqs.mods.Length);
        foreach (var mod in pqs.mods)
        {
            try
            {
                var batchMod = BatchPQSMod.Create(mod);
                if (batchMod is not null)
                    batchMods.Add(batchMod);
            }
            catch (UnsupportedPQSModException)
            {
                Debug.LogWarning(
                    $"[BurstPQS] PQSMod {mod.GetType().Name} is not supported by BatchPQS"
                );
                fallbackMessage.AppendLine(
                    $"PQSMod {mod.GetType().Name} is not supported by BatchPQS"
                );
                Fallback = true;
            }
        }

        this.mods = [.. batchMods];

        if (!Fallback)
        {
            foreach (var mod in this.mods)
            {
                try
                {
                    mod.OnSetup();
                }
                catch (UnsupportedPQSModException e)
                {
                    Debug.LogWarning(
                        $"[BatchPQS] PQSMod {mod.GetType().Name} is not supported by BatchPQS: {e.Message}"
                    );
                    fallbackMessage.AppendLine(
                        $"PQSMod {mod.GetType().Name} is not supported by BatchPQS: {e.Message}"
                    );
                    Fallback = true;
                }
            }
        }

        if (Fallback)
        {
            Debug.LogWarning(
                $"[BurstPQS] BatchPQS not supported for surface {pqs.name}. Falling back to regular PQS"
            );

            FallbackMessage = fallbackMessage.ToString();
        }
        else
        {
            Debug.Log($"[BurstPQS] BatchPQS enabled for surface {pqs.name}");
            FallbackMessage = "This planet is supported by BurstPQS";
        }
    }
    #endregion

    struct PendingBuild(BatchPQS batchPQS, PQ quad) : IDisposable
    {
        readonly PQ quad = quad;
        readonly BatchPQS batchPQS = batchPQS;
        readonly PQS pqs => batchPQS.pqs;
        MeshData meshData;
        BatchPQSJobSet jobSet;
        JobHandle handle;

        public readonly bool IsCompleted => handle.IsCompleted;
        public readonly PQ Quad => quad;

        public bool StartBuild()
        {
            if (quad.isBuilt)
                return false;
            if (quad.isSubdivided)
                return false;

            if (quad == null || quad.gameObject == null)
                return false;

            pqs.buildQuad = quad;

            meshData = MeshData.Acquire();
            jobSet = BatchPQSJobSet.Acquire();
            foreach (var mod in batchPQS.mods)
                mod.OnQuadPreBuild(quad, jobSet);

            var job = new BuildQuadJob
            {
                quadMatrix = quad.quadMatrix,
                pqsTransform = pqs.transform.localToWorldMatrix,
                inverseQuadTransform = quad.transform.worldToLocalMatrix,

                surfaceRelativeQuads = pqs.surfaceRelativeQuads,
                reqVertexMapCoords = pqs.reqVertexMapCoods,
                reqCustomNormals = pqs.reqCustomNormals,
                reqSphereUV = pqs.reqSphereUV,
                reqUVQuad = pqs.reqUVQuad,
                reqUV2 = pqs.reqUV2,
                reqUV3 = pqs.reqUV3,
                reqUV4 = pqs.reqUV4,
                reqBuildTangents = pqs.reqBuildTangents,
                reqAssignTangents = pqs.reqAssignTangents,
                reqColorChannel = pqs.reqColorChannel,

                uvSW = quad.uvSW,
                uvDelta = quad.uvDelta,

                cacheVertexCount = PQS.cacheVertCount,
                cacheSideVertCount = PQS.cacheSideVertCount,
                cacheMeshSize = PQS.cacheMeshSize,
                cacheRes = PQS.cacheRes,
                cacheTriCount = PQS.cacheTriCount,

                sphere = new(pqs),

                jobSet = new(jobSet),
                meshData = new(meshData),
                pq = new(quad),
            };

            handle = job.Schedule();

            return true;
        }

        static readonly VertexAttributeDescriptor[] AttrWithoutTangent =
        [
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 0),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, 0),
        ];

        static readonly VertexAttributeDescriptor[] AttrWithTangent =
        [
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 2),
            new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 0),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, 0),
        ];

        public void Complete()
        {
            var mesh = quad.mesh;
            mesh.Clear(false);
            handle.Complete();

            if (!meshData.interleaved.IsCreated)
                throw new Exception("mesh vertex data is empty");

            pqs.buildQuad = quad;

            int vertexCount = meshData.interleaved.Length;
            const MeshUpdateFlags flags =
                MeshUpdateFlags.DontValidateIndices
                | MeshUpdateFlags.DontResetBoneBounds
                | MeshUpdateFlags.DontNotifyMeshUsers;

            VertexAttributeDescriptor[] attrs = meshData.tangents.IsCreated
                ? AttrWithTangent
                : AttrWithoutTangent;

            mesh.SetVertexBufferParams(vertexCount, attrs);
            mesh.SetVertexBufferData(meshData.interleaved, 0, 0, vertexCount, 0, flags);
            mesh.SetVertexBufferData(meshData.normals, 0, 0, vertexCount, 1, flags);
            if (meshData.tangents.IsCreated)
                mesh.SetVertexBufferData(meshData.tangents, 0, 0, vertexCount, 2, flags);

            int indexCount = PQS.cacheIndices[0].Length;
            mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            mesh.SetIndexBufferData(PQS.cacheIndices[0], 0, 0, indexCount, flags);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), flags);
            mesh.RecalculateBounds();

            // Populate global PQS cache arrays that stock normally fills per-vertex.
            // These must be populated before OnMeshBuilt since stock PQSMods may read them.
            meshData.vertsD.CopyTo(PQS.verts);
            meshData.normals.CopyTo(PQS.normals);
            if (meshData.tangents.IsCreated)
                meshData.tangents.CopyTo(PQS.cacheTangents);
            meshData.cacheColors.CopyTo(PQS.cacheColors);
            meshData.cacheUVs.CopyTo(PQS.cacheUVs);
            meshData.cacheUV2s.CopyTo(PQS.cacheUV2s);
            meshData.cacheUV3s.CopyTo(PQS.cacheUV3s);
            meshData.cacheUV4s.CopyTo(PQS.cacheUV4s);

            quad.edgeState = PQS.EdgeState.Reset;

            jobSet.OnMeshBuilt(quad);

            foreach (var mod in batchPQS.mods)
                mod.OnQuadBuilt(quad);
            pqs.buildQuad = null;
        }

        public void Dispose()
        {
            try
            {
                handle.Complete();
            }
            finally
            {
                if (meshData is not null)
                {
                    if (batchPQS.disposeList.IsCreated)
                        batchPQS.disposeList.Add(meshData.ReleaseData());
                    else
                        meshData.Dispose();
                }
                jobSet?.Dispose();
            }
        }
    }
}
