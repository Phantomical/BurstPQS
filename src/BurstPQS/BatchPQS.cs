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
    private bool fallback = false;

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

        if (fallback || ForceFallback)
            return PQS_RevPatch.BuildQuad(pqs, quad);

        if (quad.isBuilt)
            return false;
        if (quad.isSubdivided)
            return false;

        if (quad == null || quad.gameObject == null)
            return false;

        pqs.buildQuad = quad;
        // pqs.Mod_OnQuadPreBuild(quad);

        using var meshData = new MeshData();
        using var jobSet = new BatchPQSJobSet();
        foreach (var mod in mods)
            mod.OnQuadPreBuild(quad, jobSet);

        var job = new BuildQuadJob
        {
            quadMatrix = quad.quadMatrix,
            pqsTransform = transform.localToWorldMatrix,
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
        var handle = job.Schedule();

        quad.mesh.Clear(false);

        handle.Complete();

        if (!meshData.positions.IsCreated)
            throw new Exception("mesh vertex data is empty");

        quad.mesh.SetVertices(meshData.positions);
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

        // Populate global PQS cache arrays that stock normally fills per-vertex
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

        foreach (var mod in mods)
            mod.OnQuadBuilt(quad);
        pqs.buildQuad = null;
        return true;
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
                fallback = true;
            }
        }

        if (fallback)
            Debug.LogWarning(
                $"[BurstPQS] BatchPQS not supported for surface {pqs.name}. Falling back to regular PQS"
            );
        else
            Debug.Log($"[BurstPQS] BatchPQS enabled for surface {pqs.name}");

        this.mods = [.. batchMods];

        if (!fallback)
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
                    fallback = true;
                }
            }
        }
    }
    #endregion
}
