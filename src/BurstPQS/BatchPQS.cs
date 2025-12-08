using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Vectrosity;

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

        using QuadBuildData data = new(pqs, PQS.cacheVertCount);
        using CacheData cache = new(data.VertexCount);
        using var minmax = new NativeArray<double>(2, Allocator.TempJob);

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

        try
        {
            foreach (var state in states)
                handle = state.ScheduleBuildHeights(data, handle);
            foreach (var state in states)
                handle = state.ScheduleBuildVertices(data, handle);

            var buildJob = new BuildVerticesJob
            {
                data = data.burst,
                cache = cache,
                minmax = minmax,

                pqsTransform = transform.localToWorldMatrix,
                inverseQuadTransform = data.buildQuad.transform.worldToLocalMatrix,

                surfaceRelativeQuads = pqs.surfaceRelativeQuads,
                reqCustomNormals = pqs.reqCustomNormals,
                reqSphereUV = pqs.reqSphereUV,
                reqUVQuad = pqs.reqUVQuad,
                reqUV2 = pqs.reqUV2,
                reqUV3 = pqs.reqUV3,
                reqUV4 = pqs.reqUV4,

                uvSW = quad.uvSW,
                uvDelta = quad.uvDelta,
                cacheSideVertCount = PQS.cacheSideVertCount,
            };

            handle = buildJob.Schedule(handle);
        }
        finally
        {
            handle.Complete();
        }

        CopyGeneratedData(data, cache);
        pqs.meshVertMin = Math.Min(minmax[0], pqs.meshVertMin);
        pqs.meshVertMax = Math.Max(minmax[1], pqs.meshVertMax);

        pqs.buildQuad.mesh.vertices = pqs.buildQuad.verts;
        pqs.buildQuad.mesh.triangles = PQS.cacheIndices[0];
        pqs.buildQuad.mesh.RecalculateBounds();
        pqs.buildQuad.edgeState = PQS.EdgeState.Reset;
        pqs.Mod_OnMeshBuild();

        foreach (var state in states)
            state.OnQuadBuilt(data);

        pqs.buildQuad = null;
        return true;
    }

    void CopyGeneratedData(QuadBuildData data, CacheData cache)
    {
        CopyTo(PQS.verts, cache.verts);
        CopyTo(data.buildQuad.verts, cache.quadVerts);

        if (!pqs.reqCustomNormals)
            CopyTo(PQS.normals, cache.normals);
        if (pqs.reqColorChannel)
            CopyTo(PQS.cacheColors, data.vertColor);
        if (pqs.reqSphereUV || pqs.reqUVQuad)
            CopyTo(PQS.uvs, cache.uvs);
        if (pqs.reqUV2)
            CopyTo(PQS.cacheUV2s, cache.cacheUV2s);
        if (pqs.reqUV3)
            CopyTo(PQS.cacheUV3s, cache.cacheUV3s);
        if (pqs.reqUV4)
            CopyTo(PQS.cacheUV4s, cache.cacheUV4s);
    }

    void CopyTo<T>(T[] dst, MemorySpan<T> src)
        where T : unmanaged
    {
        if (dst.Length != src.Length)
            throw new IndexOutOfRangeException("src and dst did not have the same length");

        Unsafe.CopyBlock(
            ref Unsafe.As<T, byte>(ref dst[0]),
            ref Unsafe.As<T, byte>(ref src[0]),
            (uint)dst.Length * (uint)Unsafe.SizeOf<T>()
        );
    }

    #region Jobs
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
    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public CacheData cache;

        [WriteOnly]
        public NativeArray<double> minmax;

        public Matrix4x4 pqsTransform;
        public Matrix4x4 inverseQuadTransform;

        public bool surfaceRelativeQuads;
        public bool reqCustomNormals;
        public bool reqSphereUV;
        public bool reqUVQuad;
        public bool reqUV2;
        public bool reqUV3;
        public bool reqUV4;

        public Vector2 uvSW;
        public Vector2 uvDelta;
        public int cacheSideVertCount;

        public void Execute()
        {
            if (data.VertexCount != cache.VertexCount)
            {
                Debug.LogError("[BurstPQS] Data and cache vertex counts were different");
                return;
            }

            if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
            {
                Debug.LogError("[BurstPQS] side vertex count doesn't match total vertex count");
                return;
            }

            if (surfaceRelativeQuads)
                BuildVertexSurfaceRelative();
            else
                BuildVertexHeight();

            if (!reqCustomNormals)
                BuildNormals();

            // skip reqColorChannel here because data.colors is already in the
            // format that we want.

            if (reqUVQuad)
                BuildVertexQuadUV();
            else if (reqSphereUV)
                BuildVertexSphereUV();

            if (reqUV2)
                BuildUVs(cache.cacheUV2s, data.u2, data.v2);
            if (reqUV3)
                BuildUVs(cache.cacheUV3s, data.u3, data.v3);
            if (reqUV4)
                BuildUVs(cache.cacheUV4s, data.u4, data.v4);

            var vertMax = double.MinValue;
            var vertMin = double.MaxValue;

            foreach (var height in data.vertHeight)
            {
                vertMax = Math.Max(vertMax, height);
                vertMin = Math.Min(vertMin, height);
            }

            minmax[0] = vertMin;
            minmax[1] = vertMax;
        }

        readonly void BuildVertexHeight()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var vert = data.directionFromCenter[i] * data.vertHeight[i];
                cache.verts[i] = vert;
                cache.quadVerts[i] = vert;
            }
        }

        readonly void BuildVertexSurfaceRelative()
        {
            float4x4 pqsTransform = BurstUtil.ConvertMatrix(this.pqsTransform);
            float4x4 inverseQuadTransform = BurstUtil.ConvertMatrix(this.inverseQuadTransform);

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var vert = data.directionFromCenter[i] * data.vertHeight[i];
                var prel = math.mul(
                    pqsTransform,
                    new float4(BurstUtil.ConvertVector((Vector3)vert), 1f)
                );
                var srel = math.mul(inverseQuadTransform, new float4(prel.xyz, 1f));

                cache.verts[i] = vert;
                cache.quadVerts[i] = BurstUtil.ConvertVector(srel.xyz);
            }
        }

        readonly void BuildNormals()
        {
            for (int i = 0; i < data.VertexCount; ++i)
                cache.normals[i] = data.directionFromCenter[i];
        }

        readonly void BuildVertexQuadUV()
        {
            var spacing = 1f / cacheSideVertCount;

            for (int i = 0, y = 0; y < cacheSideVertCount; ++y)
            {
                for (int x = 0; x < cacheSideVertCount; ++x, ++i)
                {
                    var cacheUV = new Vector2(x * spacing, y * spacing);
                    cache.uvs[i] = uvSW + cacheUV * uvDelta;
                }
            }
        }

        readonly void BuildVertexSphereUV()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                cache.uvs[i] = new(
                    (float)(data.latitude[i] / Math.PI + 0.5),
                    (float)(data.longitude[i] / Math.PI * 0.5)
                );
            }
        }

        readonly void BuildUVs(MemorySpan<Vector2> uvs, MemorySpan<double> u, MemorySpan<double> v)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                uvs[i] = new((float)u[i], (float)v[i]);
        }
    }
    #endregion
    #endregion

#pragma warning disable IDE1006 // Naming Styles
    readonly struct CacheData : IDisposable
    {
        readonly int _vertexCount;

        [NoAlias]
        readonly void* _data;

        public readonly int VertexCount => _vertexCount;

        #region Offsets
        const int Vector3dSize = 3 * sizeof(double);
        const int Vector3Size = 3 * sizeof(float);
        const int Vector2Size = 2 * sizeof(float);

        const int EndOff_Verts = Vector3dSize;
        const int EndOff_QuadVerts = Vector3Size + EndOff_Verts;
        const int EndOff_Normals = Vector3Size + EndOff_QuadVerts;
        const int EndOff_UVs = Vector2Size + EndOff_Normals;
        const int EndOff_CacheUV2s = Vector2Size + EndOff_UVs;
        const int EndOff_CacheUV3s = Vector2Size + EndOff_CacheUV2s;
        const int EndOff_CacheUV4s = Vector2Size + EndOff_CacheUV3s;

        const int ElemSize = EndOff_CacheUV4s;
        #endregion

        public static int GetTotalAllocationSize(int vertexCount) => vertexCount * EndOff_CacheUV4s;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly MemorySpan<T> GetOffsetSlice<T>(int endOff)
            where T : unmanaged
        {
            return new((T*)((byte*)_data + (endOff - sizeof(T)) * VertexCount), VertexCount);
        }

        public MemorySpan<Vector3d> verts => GetOffsetSlice<Vector3d>(EndOff_Verts);
        public MemorySpan<Vector3> quadVerts => GetOffsetSlice<Vector3>(EndOff_QuadVerts);
        public MemorySpan<Vector3> normals => GetOffsetSlice<Vector3>(EndOff_Normals);
        public MemorySpan<Vector2> uvs => GetOffsetSlice<Vector2>(EndOff_UVs);
        public MemorySpan<Vector2> cacheUV2s => GetOffsetSlice<Vector2>(EndOff_CacheUV2s);
        public MemorySpan<Vector2> cacheUV3s => GetOffsetSlice<Vector2>(EndOff_CacheUV3s);
        public MemorySpan<Vector2> cacheUV4s => GetOffsetSlice<Vector2>(EndOff_CacheUV4s);

        public CacheData(int vertexCount)
        {
            if (vertexCount < 0)
                throw new ArgumentOutOfRangeException(nameof(vertexCount));

            _vertexCount = vertexCount;
            _data = UnsafeUtility.Malloc(ElemSize * vertexCount, sizeof(Color), Allocator.TempJob);
        }

        public void Dispose()
        {
            UnsafeUtility.Free(_data, Allocator.TempJob);
        }
    }
#pragma warning restore IDE1006 // Naming Styles

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
                    $"[BatchPQS] PQSMod {mod.GetType().Name} is not supported by BatchPQS"
                );
                fallback = true;
            }
        }

        if (fallback)
            Debug.LogWarning(
                $"[BatchPQS] BatchPQS not supported for surface {pqs.name}. Falling back to regular PQS"
            );

        this.mods = [.. batchMods];

        if (!fallback)
        {
            foreach (var mod in this.mods)
                mod.OnSetup();
        }
    }
    #endregion
}
