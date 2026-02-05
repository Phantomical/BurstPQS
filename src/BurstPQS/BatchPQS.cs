using System;
using System.Collections.Generic;
using BurstPQS.Jobs;
using BurstPQS.Patches;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace BurstPQS;

// [BurstCompile]
public class BatchPQS : MonoBehaviour
{
    static bool ForceFallback = false;
    static readonly ProfilerMarker BuildQuadMarker = new("BatchPQS.BuildQuad");

    private PQS pqs;
    private BatchPQSMod[] mods;

    // Are there unsupported mods and do we need to fall back to the stock
    // implementation?
    private bool _fallback = false;
    private bool Fallback
    {
        get => _fallback || ForceFallback;
        set => _fallback = value;
    }

    private readonly Dictionary<PQ, PendingBuild> pending = [];

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
        else if (Fallback || ForceFallback)
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
    static readonly ProfilerMarker UpdateQuadRelativitiesMarker = new("UpdateQuadRelativities");
    static readonly ProfilerMarker QueueQuadBuildsMarker = new("QueueQuadBuilds");
    static readonly ProfilerMarker UpdateSubdivisionMarker = new("UpdateSubdivision");
    static readonly ProfilerMarker UpdateEdgesMarker = new("UpdateEdges");

    public void UpdateQuads()
    {
        if (pqs.quads == null)
            return;

        using var scope = UpdateQuadsMarker.Auto();

        pqs.isThinking = true;
        pqs.maxFrameEnd = Time.realtimeSinceStartup + pqs.maxFrameTime;

        SortQuadsByDistance(pqs.quads, pqs.relativeTargetPosition);

        // Update quad relativities first
        using (UpdateQuadRelativitiesMarker.Auto())
        {
            for (int i = 0; i < Mathf.Min(pqs.quads.Length, 5); i++)
                UpdateQuadRelativities(pqs.quads[i]);
        }

        // Now queue up all pending quad builds in advance.
        using (QueueQuadBuildsMarker.Auto())
        {
            int count = 0;
            for (int i = 0; i < Mathf.Min(pqs.quads.Length, 5); i++)
                QueueQuadBuilds(pqs.quads[i], ref count);
            JobHandle.ScheduleBatchedJobs();
        }

        // Finally we can actually run the stock update
        using (UpdateSubdivisionMarker.Auto())
        {
            for (int i = 0; i < Mathf.Min(pqs.quads.Length, 5); i++)
                pqs.quads[i].UpdateSubdivision();
        }

        if (pqs.reqCustomNormals)
        {
            using (UpdateEdgesMarker.Auto())
                pqs.UpdateEdges();
        }

        pqs.isThinking = false;
    }

    void UpdateQuadRelativities(PQ quad)
    {
        quad.UpdateTargetRelativity();

        if (quad.isSubdivided)
        {
            foreach (var child in quad.subNodes)
                UpdateQuadRelativities(child);
        }
    }

    void QueueQuadBuilds(PQ quad, ref int count)
    {
        if (quad.isSubdivided)
        {
            foreach (var child in quad.subNodes)
                QueueQuadBuilds(child, ref count);
            return;
        }

        if (quad.isBuilt)
            return;
        if (quad.subdivision < pqs.minLevel)
            return;

        var threshold = pqs.subdivisionThresholds[quad.subdivision] * quad.subdivideThresholdFactor;
        if (quad.gcDist < threshold && quad.subdivision < pqs.maxLevelAtCurrentTgtSpeed)
            return;

        if (quad.gcd1 >= pqs.visibleRadius)
            return;

        if (pending.ContainsKey(quad))
            return;

        var build = new PendingBuild(this, quad);
        if (!build.StartBuild())
            return;

        count += 1;
        if (count > 10)
        {
            JobHandle.ScheduleBatchedJobs();
            count = 0;
        }

        pending.Add(quad, build);
    }

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
                Fallback = true;
            }
        }

        if (Fallback)
            Debug.LogWarning(
                $"[BurstPQS] BatchPQS not supported for surface {pqs.name}. Falling back to regular PQS"
            );
        else
            Debug.Log($"[BurstPQS] BatchPQS enabled for surface {pqs.name}");

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
                    Fallback = true;
                }
            }
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

        public bool IsCompleted => handle.IsCompleted;

        public bool StartBuild()
        {
            if (quad.isBuilt)
                return false;
            if (quad.isSubdivided)
                return false;

            if (quad == null || quad.gameObject == null)
                return false;

            pqs.buildQuad = quad;

            meshData = new MeshData();
            jobSet = new BatchPQSJobSet();
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

        public void Complete()
        {
            quad.mesh.Clear(false);
            handle.Complete();

            if (!meshData.verts.IsCreated)
                throw new Exception("mesh vertex data is empty");

            pqs.buildQuad = quad;

            quad.mesh.SetVertices(meshData.verts);
            quad.mesh.SetNormals(meshData.normals);

            if (meshData.tangents.IsCreated)
                quad.mesh.SetTangents(meshData.tangents);
            if (meshData.colors.IsCreated)
                quad.mesh.SetColors(meshData.colors);
            if (meshData.uv0.IsCreated)
                quad.mesh.SetUVs(0, meshData.uv0);
            if (meshData.uv1.IsCreated)
                quad.mesh.SetUVs(1, meshData.uv1);
            if (meshData.uv2.IsCreated)
                quad.mesh.SetUVs(2, meshData.uv2);
            if (meshData.uv3.IsCreated)
                quad.mesh.SetUVs(3, meshData.uv3);

            // Populate global PQS cache arrays that stock normally fills per-vertex.
            // These must be populated before OnMeshBuilt since stock PQSMods may read them.
            meshData.vertsD.CopyTo(PQS.verts);
            meshData.normals.CopyTo(PQS.normals);
            if (meshData.colors.IsCreated)
                meshData.colors.CopyTo(PQS.cacheColors);
            if (meshData.tangents.IsCreated)
                meshData.tangents.CopyTo(PQS.cacheTangents);
            if (meshData.uv1.IsCreated)
                meshData.uv1.CopyTo(PQS.cacheUV2s);
            if (meshData.uv2.IsCreated)
                meshData.uv2.CopyTo(PQS.cacheUV3s);
            if (meshData.uv3.IsCreated)
                meshData.uv3.CopyTo(PQS.cacheUV4s);

            quad.mesh.SetTriangles(PQS.cacheIndices[0], 0);
            quad.mesh.RecalculateBounds();
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
                meshData?.Dispose();
                jobSet?.Dispose();
            }
        }
    }
}
