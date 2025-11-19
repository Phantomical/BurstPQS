using System;
using System.Collections.Generic;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS;

[BurstCompile]
public unsafe class BatchPQS : MonoBehaviour
{
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
        if (quad.isBuilt)
            return false;
        if (quad.isSubdivided)
            return false;

        if (quad == null || quad.gameObject == null)
            return false;

        pqs.buildQuad = quad;
        pqs.Mod_OnQuadPreBuild(quad);

        using QuadBuildData data = new(pqs, PQS.cacheVertCount);

        var states = new List<IBatchPQSModState>(mods.Length);
        foreach (var mod in mods)
        {
            var state = mod.OnQuadPreBuild(data);
            if (state is not null)
                states.Add(state);
        }

        var initJob = new InitBuildDataJob
        {
            quadMatrix = quad.quadMatrix,
            data = data.burst,
            reqVertexMapCoords = pqs.reqVertexMapCoods,
            cacheSideVertCount = PQS.cacheSideVertCount,
            cacheMeshSize = PQS.cacheMeshSize,
        };

        JobHandle handle = initJob.Schedule();

        foreach (var state in states)
            handle = state.ScheduleBuildHeights(data, handle);
        foreach (var state in states)
            handle = state.ScheduleBuildVertices(data, handle);

        using var buffer = new OwnedBuffer(
            BurstQuadBuildDataV1.GetRequiredBufferSize(PQS.cacheVertCount),
            Allocator.TempJob
        );
        buffer.Clear();

        QuadBuildDataV1 data = default;
        data.buildQuad = quad;
        data.burstData = new(quad, buffer.Data, buffer.Length, PQS.cacheVertCount);

        InitBuildData(quad, in data);

        foreach (var mod in mods)
            mod.OnBatchVertexBuildHeight(in data);
        foreach (var mod in mods)
            mod.OnBatchVertexBuild(in data);

        if (!pqs.isFakeBuild)
            BuildVertices(in data);

        pqs.buildQuad.mesh.vertices = pqs.buildQuad.verts;
        pqs.buildQuad.mesh.triangles = PQS.cacheIndices[0];
        pqs.buildQuad.mesh.RecalculateBounds();
        pqs.buildQuad.edgeState = PQS.EdgeState.Reset;
        pqs.Mod_OnMeshBuild();
        pqs.Mod_OnQuadBuilt(quad);
        pqs.buildQuad = null;
        return true;
    }

    #region Burst Methods
    #region InitBuildData
    [BurstCompile]
    struct InitBuildDataJob : IJob
    {
        public Matrix4x4 quadMatrix;
        public BurstQuadBuildData data;
        public bool reqVertexMapCoords;
        public int cacheSideVertCount;
        public float cacheMeshSize;

        public void Execute()
        {
            if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
            {
                Debug.LogError("[BurstPQS] CacheVerts length was not equal to data vertex count");
                return;
            }

            float spacing = cacheMeshSize / (cacheSideVertCount - 1);
            float halfSize = cacheMeshSize * 0.5f;
            for (int i = 0, y = 0; y < cacheSideVertCount; ++y)
            {
                for (int x = 0; x < cacheSideVertCount; ++x, ++i)
                {
                    var vert = new Vector3(halfSize - x * spacing, 0f, halfSize - y * spacing);
                    var globalV = quadMatrix.MultiplyPoint3x4(vert);

                    data.globalV[i] = globalV;
                    data.directionFromCenter[i] = globalV.normalized;
                }
            }

            // Set other fields individually so that they can be vectorized
            data.vertHeight.Fill(data.sphere.radius);
            data.allowScatter.Fill(true);

            if (!reqVertexMapCoords)
                return;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var latitude = Math.Asin(MathUtil.Clamp01(data.directionFromCenter[i].y));
                var directionXZ = new Vector3d(
                    data.directionFromCenter[i].x,
                    0.0,
                    data.directionFromCenter[i].z
                );

                double longitude;
                if (directionXZ.sqrMagnitude == 0.0)
                    longitude = 0.0;
                else if (directionXZ.z < 0.0)
                    longitude = Math.PI - Math.Asin(directionXZ.x / directionXZ.magnitude);
                else
                    longitude = Math.Asin(directionXZ.x / directionXZ.magnitude);

                data.latitude[i] = latitude;
                data.longitude[i] = longitude;
                data.u[i] = latitude / Math.PI + 0.5;
                data.v[i] = longitude / Math.PI * 0.5;
            }
        }
    }

    void InitBuildData(PQ quad, in QuadBuildDataV1 data)
    {
        fixed (Vector3* pCacheVerts = PQS.cacheVerts)
        {
            InitBuildDataBurst(
                new MemorySpan<Vector3>(pCacheVerts, PQS.cacheVerts.Length),
                in quad.quadMatrix,
                in data.burstData,
                pqs.radius,
                pqs.reqVertexMapCoods
            );
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void InitBuildDataBurst(
        in MemorySpan<Vector3> cacheVerts,
        in Matrix4x4 quadMatrix,
        in BurstQuadBuildDataV1 data,
        double radius,
        bool reqVertexMapCoords
    )
    {
        int cacheVertCount = cacheVerts.Length;
        for (int i = 0; i < cacheVertCount; ++i)
        {
            var globalV = quadMatrix.MultiplyPoint3x4(cacheVerts[i]);

            data.globalV[i] = globalV;
            data.directionFromCenter[i] = globalV.normalized;
        }

        // Set other fields individually so that they can be vectorized
        data.vertHeight.Fill(radius);
        data.allowScatter.Fill(true);

        if (!reqVertexMapCoords)
            return;

        for (int i = 0; i < cacheVertCount; ++i)
        {
            var latitude = Math.Asin(MathUtil.Clamp01(data.directionFromCenter[i].y));
            var directionXZ = new Vector3d(
                data.directionFromCenter[i].x,
                0.0,
                data.directionFromCenter[i].z
            );

            double longitude;
            if (directionXZ.sqrMagnitude == 0.0)
                longitude = 0.0;
            else if (directionXZ.z < 0.0)
                longitude = Math.PI - Math.Asin(directionXZ.x / directionXZ.magnitude);
            else
                longitude = Math.Asin(directionXZ.x / directionXZ.magnitude);

            data.latitude[i] = latitude;
            data.longitude[i] = longitude;
            data.u[i] = latitude / Math.PI + 0.5;
            data.v[i] = longitude / Math.PI * 0.5;
        }
    }
    #endregion

    #region BuildVertices
    struct BuildVerticesOptions
    {
        public bool surfaceRelativeQuads;
        public bool reqCustomNormals;
        public bool reqColorChannel;
        public bool reqSphereUV;
        public bool reqUVQuad;
        public bool reqUV2;
        public bool reqUV3;
        public bool reqUV4;

        public Vector2 uvSW;
        public Vector2 uvDelta;

        public MemorySpan<Vector3d> verts;
        public MemorySpan<Vector3> quadVerts;
        public MemorySpan<Vector3> normals;
        public MemorySpan<Color> colors;
        public MemorySpan<Vector2> uvs;
        public MemorySpan<Vector2> cacheUVs;
        public MemorySpan<Vector2> cacheUV2s;
        public MemorySpan<Vector2> cacheUV3s;
        public MemorySpan<Vector2> cacheUV4s;
        public Matrix4x4 pqsTransform;
        public Matrix4x4 invQuadTransform;
    }

    struct BuildVerticesOutputs
    {
        public double meshVertMin;
        public double meshVertMax;
    }

    unsafe void BuildVertices(in QuadBuildDataV1 data)
    {
        fixed (Vector3d* pverts = PQS.verts)
        fixed (Vector3* pQuadVerts = data.buildQuad.verts)
        fixed (Vector3* pNormals = PQS.normals)
        fixed (Color* pColors = PQS.cacheColors)
        fixed (Vector2* pUvs = PQS.uvs)
        fixed (Vector2* pCacheUVs = PQS.cacheUVs)
        fixed (Vector2* pCacheUV2s = PQS.cacheUV2s)
        fixed (Vector2* pCacheUV3s = PQS.cacheUV3s)
        fixed (Vector2* pCacheUV4s = PQS.cacheUV4s)
        {
            var opts = new BuildVerticesOptions
            {
                surfaceRelativeQuads = pqs.surfaceRelativeQuads,
                reqCustomNormals = pqs.reqCustomNormals,
                reqColorChannel = pqs.reqColorChannel,
                reqSphereUV = pqs.reqSphereUV,
                reqUVQuad = pqs.reqUVQuad,
                reqUV2 = pqs.reqUV2,
                reqUV3 = pqs.reqUV3,
                reqUV4 = pqs.reqUV4,

                uvSW = data.buildQuad.uvSW,
                uvDelta = data.buildQuad.uvDelta,

                verts = new(pverts, PQS.verts.Length),
                quadVerts = new(pQuadVerts, data.buildQuad.verts.Length),
                normals = new(pNormals, PQS.normals.Length),
                colors = new(pColors, PQS.cacheColors.Length),
                uvs = new(pUvs, PQS.uvs.Length),
                cacheUVs = new(pCacheUV2s, PQS.cacheUVs.Length),
                cacheUV2s = new(pCacheUV2s, PQS.cacheUV2s.Length),
                cacheUV3s = new(pCacheUV3s, PQS.cacheUV3s.Length),
                cacheUV4s = new(pCacheUV4s, PQS.cacheUV4s.Length),

                pqsTransform = transform.localToWorldMatrix,
                invQuadTransform = data.buildQuad.transform.worldToLocalMatrix,
            };

            BuildVerticesBurst(in data.burstData, in opts, out var outputs);

            pqs.buildQuad.meshVertMax = outputs.meshVertMax;
            pqs.buildQuad.meshVertMin = outputs.meshVertMin;
        }
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVerticesBurst(
        in BurstQuadBuildDataV1 data,
        in BuildVerticesOptions opts,
        out BuildVerticesOutputs outputs
    )
    {
        var vertexCount = data.VertexCount;

        if (opts.surfaceRelativeQuads)
        {
            float4x4 pqsTransform = BurstUtil.ConvertMatrix(opts.pqsTransform);
            float4x4 invQuadTransform = BurstUtil.ConvertMatrix(opts.invQuadTransform);

            for (int i = 0; i < vertexCount; ++i)
            {
                var vert = data.directionFromCenter[i] * data.vertHeight[i];
                var prel = math.mul(
                    pqsTransform,
                    new float4(BurstUtil.ConvertVector((Vector3)vert), 1f)
                );
                var srel = math.mul(invQuadTransform, new float4(prel.xyz, 1f));

                opts.verts[i] = vert;
                opts.quadVerts[i] = BurstUtil.ConvertVector(srel.xyz);
            }
        }
        else
        {
            for (int i = 0; i < vertexCount; ++i)
            {
                var vert = data.directionFromCenter[i] * data.vertHeight[i];
                opts.verts[i] = vert;
                opts.quadVerts[i] = vert;
            }
        }

        if (!opts.reqCustomNormals)
        {
            for (int i = 0; i < vertexCount; ++i)
                opts.normals[i] = data.directionFromCenter[i];
        }

        if (opts.reqColorChannel)
        {
            for (int i = 0; i < vertexCount; ++i)
                opts.colors[i] = data.vertColor[i];
        }

        if (opts.reqUVQuad)
        {
            for (int i = 0; i < vertexCount; ++i)
                opts.uvs[i] = opts.uvSW + opts.cacheUVs[i] * opts.uvDelta;
        }
        else if (opts.reqSphereUV)
        {
            for (int i = 0; i < vertexCount; ++i)
            {
                opts.uvs[i] = new(
                    (float)(data.latitude[i] / Math.PI + 0.5),
                    (float)(data.longitude[i] / Math.PI * 0.5)
                );
            }
        }

        if (opts.reqUV2)
        {
            for (int i = 0; i < vertexCount; ++i)
                opts.cacheUV2s[i] = new((float)data.u2[i], (float)data.v2[i]);
        }

        if (opts.reqUV3)
        {
            for (int i = 0; i < vertexCount; ++i)
                opts.cacheUV3s[i] = new((float)data.u3[i], (float)data.v3[i]);
        }

        if (opts.reqUV4)
        {
            for (int i = 0; i < vertexCount; ++i)
                opts.cacheUV4s[i] = new((float)data.u4[i], (float)data.v4[i]);
        }

        var vertMax = double.MinValue;
        var vertMin = double.MaxValue;

        foreach (var height in data.vertHeight)
        {
            vertMax = Math.Max(vertMax, height);
            vertMin = Math.Min(vertMin, height);
        }

        outputs = new() { meshVertMax = vertMax, meshVertMin = vertMin };
    }
    #endregion
    #endregion

    #region Method Injections
    internal void PostSetupMods()
    {
        List<BatchPQSModV1> batchMods = new(pqs.mods.Length);
        foreach (var mod in pqs.mods)
        {
            var batchMod = BatchPQSModV1.Create(mod);
            if (batchMod is not null)
                batchMods.Add(batchMod);
        }

        this.mods = [.. batchMods];

        foreach (var mod in this.mods)
            mod.OnSetup();
    }
    #endregion
}
