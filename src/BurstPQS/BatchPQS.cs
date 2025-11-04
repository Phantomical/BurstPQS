using System;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS;

[BurstCompile]
public unsafe class BatchPQS : PQS
{
    private IBatchPQSMod[] batchMods;

    void Awake()
    {
        // Get the PQS component that we are replacing, copy all its members to
        // ourselves, and then destroy it immediately.
        var pqs = GetComponent<PQS>();

        MemberwiseCloneFrom(pqs);

        pqs.enabled = false;
        DestroyImmediate(pqs);
    }

    public new bool BuildQuad(PQ quad)
    {
        if (quad.isBuilt)
            return false;
        if (quad.isSubdivided)
            return false;

        if (quad == null || quad.gameObject == null)
            return false;

        buildQuad = quad;
        Mod_OnQuadPreBuild(quad);

        using var buffer = new OwnedBuffer(
            BurstQuadBuildData.GetRequiredBufferSize(cacheVertCount),
            Allocator.TempJob
        );
        buffer.Clear();

        QuadBuildData data = default;
        data.buildQuad = quad;
        data.burstData = new(quad, buffer.Data, buffer.Length, cacheVertCount);

        InitBuildData(quad, in data);

        foreach (var mod in batchMods)
            mod.OnQuadBuildVertexHeight(in data);
        foreach (var mod in batchMods)
            mod.OnQuadBuildVertex(in data);

        BuildVertices(in data);

        buildQuad.mesh.vertices = buildQuad.verts;
        buildQuad.mesh.triangles = cacheIndices[0];
        buildQuad.mesh.RecalculateBounds();
        buildQuad.edgeState = EdgeState.Reset;
        Mod_OnMeshBuild();
        Mod_OnQuadBuilt(quad);
        buildQuad = null;
        return true;
    }

    #region Burst Methods
    #region InitBuildData
    delegate void InitBuildDataDelegate(
        in MemorySpan<Vector3> cacheVerts,
        in Matrix4x4 quadMatrix,
        in QuadBuildData data,
        double radius,
        bool reqVertexMapCoords
    );

    static readonly InitBuildDataDelegate InitBuildDataFp = BurstCompiler
        .CompileFunctionPointer<InitBuildDataDelegate>(InitBuildDataBurst)
        .Invoke;

    void InitBuildData(PQ quad, in QuadBuildData data)
    {
        fixed (Vector3* pCacheVerts = cacheVerts)
        {
            InitBuildDataFp(
                new MemorySpan<Vector3>(pCacheVerts, cacheVerts.Length),
                in quad.quadMatrix,
                in data,
                radius,
                reqVertexMapCoods
            );
        }
    }

    [BurstCompile]
    static void InitBuildDataBurst(
        in MemorySpan<Vector3> cacheVerts,
        in Matrix4x4 quadMatrix,
        in QuadBuildData data,
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
            var latitude = Math.Asin(UtilMath.Clamp01(data.directionFromCenter[i].y));
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

    unsafe void BuildVertices(in QuadBuildData data)
    {
        fixed (Vector3d* pverts = verts)
        fixed (Vector3* pQuadVerts = data.buildQuad.verts)
        fixed (Vector3* pNormals = normals)
        fixed (Color* pColors = cacheColors)
        fixed (Vector2* pUvs = uvs)
        fixed (Vector2* pCacheUVs = cacheUVs)
        fixed (Vector2* pCacheUV2s = cacheUV2s)
        fixed (Vector2* pCacheUV3s = cacheUV3s)
        fixed (Vector2* pCacheUV4s = cacheUV4s)
        {
            var opts = new BuildVerticesOptions
            {
                surfaceRelativeQuads = surfaceRelativeQuads,
                reqCustomNormals = reqCustomNormals,
                reqColorChannel = reqColorChannel,
                reqSphereUV = reqSphereUV,
                reqUVQuad = reqUVQuad,
                reqUV2 = reqUV2,
                reqUV3 = reqUV3,
                reqUV4 = reqUV4,

                uvSW = data.buildQuad.uvSW,
                uvDelta = data.buildQuad.uvDelta,

                verts = new(pverts, verts.Length),
                quadVerts = new(pQuadVerts, data.buildQuad.verts.Length),
                normals = new(pNormals, normals.Length),
                colors = new(pColors, cacheColors.Length),
                uvs = new(pUvs, uvs.Length),
                cacheUVs = new(pCacheUV2s, cacheUVs.Length),
                cacheUV2s = new(pCacheUV2s, cacheUV2s.Length),
                cacheUV3s = new(pCacheUV3s, cacheUV3s.Length),
                cacheUV4s = new(pCacheUV4s, cacheUV4s.Length),

                pqsTransform = transform.localToWorldMatrix,
                invQuadTransform = data.buildQuad.transform.worldToLocalMatrix,
            };

            BuildVerticesBurst(in data.burstData, in opts, out var outputs);

            buildQuad.meshVertMax = outputs.meshVertMax;
            buildQuad.meshVertMin = outputs.meshVertMin;
        }
    }

    [BurstCompile]
    static void BuildVerticesBurst(
        in BurstQuadBuildData data,
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
        batchMods = new IBatchPQSMod[mods.Length];
        for (int i = 0; i < batchMods.Length; ++i)
        {
            if (mods[i] is IBatchPQSMod batchMod)
                batchMods[i] = batchMod;
            else
                batchMods[i] = new Mod.Shim(mods[i]);
        }
    }
    #endregion

    #region PQS Memberwise Clone
    private void MemberwiseCloneFrom(PQS instance) => CloneUtil.MemberwiseCopy(instance, this);
    #endregion
}
