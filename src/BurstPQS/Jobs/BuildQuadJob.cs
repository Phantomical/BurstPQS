using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using static PQ;

namespace BurstPQS.Jobs;

[StructLayout(LayoutKind.Sequential)]
internal struct BuildQuadJob : IJob
{
    static readonly ProfilerMarker InitHeightDataMarker = new("InitHeightData");
    static readonly ProfilerMarker InitVertexDataMarker = new("InitVertexData");
    static readonly ProfilerMarker InitMeshDataMarker = new("InitMeshData");
    static readonly ProfilerMarker BuildMeshMarker = new("BuildMesh");

    public Matrix4x4 quadMatrix;
    public Matrix4x4 pqsTransform;
    public Matrix4x4 inverseQuadTransform;

    public bool surfaceRelativeQuads;
    public bool reqVertexMapCoords;
    public bool reqCustomNormals;
    public bool reqSphereUV;
    public bool reqUVQuad;
    public bool reqUV2;
    public bool reqUV3;
    public bool reqUV4;
    public bool reqBuildTangents;
    public bool reqAssignTangents;
    public bool reqColorChannel;

    public Vector2 uvSW;
    public Vector2 uvDelta;

    public int cacheVertexCount;
    public int cacheSideVertCount;
    public float cacheMeshSize;
    public int cacheRes;
    public int cacheTriCount;

    public SphereData sphere;

    public ObjectHandle<BatchPQSJobSet> jobSet;
    public ObjectHandle<MeshData> meshData;
    public ObjectHandle<PQ> pq;

    public BuildQuadJob()
    {
        BuildQuadJobExt.Init();
    }

    #region Execute
    public unsafe void Execute()
    {
        using var jsguard = this.jobSet;
        using var mdguard = this.meshData;
        using var pqguard = this.pq;

        var jobSet = this.jobSet.Target;
        var meshOutputData = this.meshData.Target;
        var pq = this.pq.Target;

        var heightData = new BuildHeightsData(sphere, cacheVertexCount);
        using (InitHeightDataMarker.Auto())
            this.InitHeightData(ref heightData);
        jobSet.BuildHeights(in heightData);

        var vertexData = new BuildVerticesData(heightData);
        using (InitVertexDataMarker.Auto())
            this.InitVertexData(ref vertexData);
        jobSet.BuildVertices(in vertexData);

        fixed (int* pindices = PQS.cacheIndices[0])
        fixed (Vector3* ptan2 = PQS.tan2)
        {
            var indices = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(
                pindices,
                PQS.cacheIndices[0].Length,
                Allocator.Invalid
            );
            var tan2 = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector3>(
                ptan2,
                PQS.tan2.Length,
                Allocator.Invalid
            );

            var meshData = new BuildMeshData(vertexData);
            using (InitMeshDataMarker.Auto())
                this.InitMeshData(ref meshData, tan2, indices);
            jobSet.BuildMesh(in meshData);

            using (BuildMeshMarker.Auto())
                this.BuildMesh(ref meshData, ref meshOutputData.data);

            // Populate shared quad arrays that stock PQS normally fills
            meshData.verts.AsNativeArray().CopyTo(pq.verts);
            meshData.normals.AsNativeArray().CopyTo(pq.vertNormals);

            pq.meshVertMax = meshData.VertMax;
            pq.meshVertMin = meshData.VertMin;
        }

        // Backup edge normals after the normals have been copied to pq.vertNormals
        if (reqCustomNormals)
            BackupEdgeNormals();
    }
    #endregion

    #region InitHeightData
    internal readonly void InitHeightDataImpl(ref BuildHeightsData data)
    {
        ref readonly var self = ref this;

        if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
        {
            ThrowMismatchedCacheVerts();
            Hint.Assume(false);
        }

        float spacing = cacheMeshSize / (cacheSideVertCount - 1);
        float halfSize = cacheMeshSize * 0.5f;

        for (int i = 0, y = 0; y < cacheSideVertCount; ++y)
        {
            for (int x = 0; x < cacheSideVertCount; ++x, ++i)
            {
                var vert = new Vector3(halfSize - x * spacing, 0f, halfSize - y * spacing);
                var globalV = quadMatrix.MultiplyPoint3x4(vert);

                data.directionFromCenter[i] = globalV.normalized;
            }
        }

        // Set other fields individually so that they can be vectorized
        data.vertHeight.Fill(data.sphere.radius);
        data.vertColor.Clear();

        if (!reqVertexMapCoords)
        {
            data.latitude.Clear();
            data.longitude.Clear();
            data.u.Clear();
            data.v.Clear();
            data.sx.Clear();
            data.sy.Clear();
            return;
        }

        for (int i = 0; i < data.VertexCount; ++i)
        {
            var latitude = Math.Asin(MathUtil.Clamp(data.directionFromCenter[i].y, -1.0, 1.0));
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
            data.u[i] = longitude / Math.PI * 0.5;
            data.v[i] = latitude / Math.PI + 0.5;
            data.sx[i] = data.u[i];
            data.sy[i] = data.v[i];
        }
    }
    #endregion

    #region InitVertexData
    internal readonly void InitVertexDataImpl(ref BuildVerticesData data)
    {
        data.u2.Clear();
        data.v2.Clear();
        data.u3.Clear();
        data.v3.Clear();
        data.u4.Clear();
        data.v4.Clear();

        data.allowScatter.Fill(true);
    }
    #endregion

    #region InitMeshData
    internal readonly void InitMeshDataImpl(
        ref BuildMeshData data,
        NativeArray<Vector3> tan2,
        NativeArray<int> indices
    )
    {
        if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
        {
            ThrowMismatchedVertexCount();
            Hint.Assume(false);
        }

        if (surfaceRelativeQuads)
            BuildVertexSurfaceRelative(in data);
        else
            BuildVertexHeight(in data);

        BuildNormals(in data);

        if (reqUVQuad)
            BuildVertexQuadUV(in data);
        else if (reqSphereUV)
            BuildVertexSphereUV(in data);
        else
            data.uvs.Clear();

        if (reqUV2)
            BuildUVs(in data, data.uv2s, data.u2, data.v2);
        else
            data.uv2s.Clear();

        if (reqUV3)
            BuildUVs(in data, data.uv3s, data.u3, data.v3);
        else
            data.uv3s.Clear();

        if (reqUV4)
            BuildUVs(in data, data.uv4s, data.u4, data.v4);
        else
            data.uv4s.Clear();

        if (reqCustomNormals)
            BuildMeshNormals(in data, indices);

        if (reqBuildTangents)
            BuildMeshTangents(in data, tan2);

        var vertMax = double.MinValue;
        var vertMin = double.MaxValue;

        foreach (var vheight in data.vertHeight)
        {
            var height = vheight - data.sphere.radius;

            vertMax = Math.Max(vertMax, height);
            vertMin = Math.Min(vertMin, height);
        }

        data.VertMax = vertMax;
        data.VertMin = vertMin;
    }

    readonly void BuildVertexHeight(in BuildMeshData data)
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var vert = data.directionFromCenter[i] * data.vertHeight[i];
            data.vertsD[i] = vert;
            data.verts[i] = vert;
        }
    }

    readonly void BuildVertexSurfaceRelative(in BuildMeshData data)
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

            data.vertsD[i] = vert;
            data.verts[i] = BurstUtil.ConvertVector(srel.xyz);
        }
    }

    readonly void BuildNormals(in BuildMeshData data)
    {
        for (int i = 0; i < data.VertexCount; ++i)
            data.normals[i] = data.directionFromCenter[i];
    }

    readonly void BuildVertexQuadUV(in BuildMeshData data)
    {
        var spacing = 1f / cacheSideVertCount;

        for (int i = 0, y = 0; y < cacheSideVertCount; ++y)
        {
            for (int x = 0; x < cacheSideVertCount; ++x, ++i)
            {
                var cacheUV = new Vector2(x * spacing, y * spacing);
                data.uvs[i] = uvSW + cacheUV * uvDelta;
            }
        }
    }

    readonly void BuildVertexSphereUV(in BuildMeshData data)
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.uvs[i] = new(
                (float)(data.longitude[i] / Math.PI * 0.5),
                (float)(data.latitude[i] / Math.PI + 0.5)
            );
        }
    }

    readonly void BuildUVs(
        in BuildMeshData data,
        MemorySpan<Vector2> uvs,
        MemorySpan<double> u,
        MemorySpan<double> v
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
            uvs[i] = new((float)u[i], (float)v[i]);
    }
    #endregion

    #region BuildMesh
    internal readonly void BuildMeshImpl(ref BuildMeshData data, ref MeshDataStruct mesh)
    {
        var positions = new NativeArray<Vector3>(data.VertexCount, Allocator.Persistent);
        mesh.verts = positions;
        CopyToNativeArray(positions, data.verts);

        var positionsD = new NativeArray<Vector3d>(data.VertexCount, Allocator.Persistent);
        mesh.vertsD = positionsD;
        CopyToNativeArray(positionsD, data.vertsD);

        var normals = new NativeArray<Vector3>(data.VertexCount, Allocator.Persistent);
        mesh.normals = normals;
        normals.CopyFrom(data.normals.AsNativeArray());

        if (reqAssignTangents)
        {
            var tangents = new NativeArray<Vector4>(data.VertexCount, Allocator.Persistent);
            mesh.tangents = tangents;
            tangents.CopyFrom(data.tangents.AsNativeArray());
        }

        if (reqColorChannel)
        {
            var colors = new NativeArray<Color>(data.VertexCount, Allocator.Persistent);
            mesh.colors = colors;
            CopyToNativeArray(colors, data.vertColor);
        }

        if (reqSphereUV || reqUVQuad)
        {
            var uv0 = new NativeArray<Vector2>(data.VertexCount, Allocator.Persistent);
            mesh.uv0 = uv0;
            CopyToNativeArray(uv0, data.uvs);
        }

        if (reqUV2)
        {
            mesh.uv1 = new NativeArray<Vector2>(data.VertexCount, Allocator.Persistent);
            CopyToNativeArray(mesh.uv1, data.uv2s);
        }

        if (reqUV3)
        {
            mesh.uv2 = new NativeArray<Vector2>(data.VertexCount, Allocator.Persistent);
            CopyToNativeArray(mesh.uv2, data.uv3s);
        }

        if (reqUV4)
        {
            mesh.uv3 = new NativeArray<Vector2>(data.VertexCount, Allocator.Persistent);
            CopyToNativeArray(mesh.uv3, data.uv4s);
        }
    }

    readonly void BuildMeshNormals(in BuildMeshData data, NativeArray<int> indices)
    {
        var triNormals = new NativeArray<Vector3>(
            cacheTriCount,
            Allocator.Temp,
            NativeArrayOptions.UninitializedMemory
        );
        var vertNormals = data.normals;

        for (int i = 0, j = 0; i < cacheTriCount; i++, j += 3)
        {
            // Use planet-relative positions (vertsD) to match stock PQS.BuildNormals,
            // which computes face normals from PQS.verts (planet-relative).
            var ba = (Vector3)(data.vertsD[indices[j + 1]] - data.vertsD[indices[j]]);
            var ca = (Vector3)(data.vertsD[indices[j + 2]] - data.vertsD[indices[j]]);

            triNormals[i] = Vector3.Cross(ba, ca).normalized;
        }

        vertNormals.Clear();

        for (int i = 0, j = 0; i < cacheTriCount; i++, j += 3)
        {
            vertNormals[indices[j + 0]] += triNormals[i];
            vertNormals[indices[j + 1]] += triNormals[i];
            vertNormals[indices[j + 2]] += triNormals[i];
        }

        for (int i = 0; i < data.VertexCount; ++i)
            vertNormals[i] = vertNormals[i].normalized;
    }

    readonly void BuildMeshTangents(in BuildMeshData data, NativeArray<Vector3> tan2) =>
        BuildTangents(data.normals.AsNativeArray(), data.tangents.AsNativeArray(), tan2);

    internal static void BuildTangents(
        [NoAlias] NativeArray<Vector3> normals,
        [NoAlias] NativeArray<Vector4> tangents,
        [NoAlias] NativeArray<Vector3> tan2
    )
    {
        for (int i = 0; i < tangents.Length; ++i)
        {
            var normal = normals[i];
            var tangent = Vector3.zero;
            Vector3.OrthoNormalize(ref normal, ref tangent);

            tangents[i] = new Vector4(
                tangent.x,
                tangent.y,
                tangent.z,
                (Vector3.Dot(Vector3.Cross(normal, tangent), tan2[i]) < 0f) ? -1f : 1f
            );
        }
    }
    #endregion

    static unsafe void CopyToNativeArray<T>(NativeArray<T> dest, MemorySpan<T> src)
        where T : unmanaged
    {
        if (dest.Length != src.Length)
            ThrowArraySizeMismatch();

        UnsafeUtility.MemCpy(dest.GetUnsafePtr(), src.GetDataPtr(), sizeof(T) * src.Length);
    }

    readonly void BackupEdgeNormals()
    {
        var buildQuad = pq.Target;

        for (int i = 0; i < 4; i++)
        {
            switch ((QuadEdge)i)
            {
                case QuadEdge.North:
                    BackupEdgeNormals(buildQuad.edgeNormals[i], cacheSideVertCount, vi(0, 0), 1);
                    break;
                case QuadEdge.South:
                    BackupEdgeNormals(
                        buildQuad.edgeNormals[i],
                        cacheSideVertCount,
                        vi(cacheRes, cacheRes),
                        -1
                    );
                    break;
                case QuadEdge.East:
                    BackupEdgeNormals(
                        buildQuad.edgeNormals[i],
                        cacheSideVertCount,
                        vi(0, cacheRes),
                        -cacheSideVertCount
                    );
                    break;
                case QuadEdge.West:
                    BackupEdgeNormals(
                        buildQuad.edgeNormals[i],
                        cacheSideVertCount,
                        vi(cacheRes, 0),
                        cacheSideVertCount
                    );
                    break;
            }
        }
    }

    readonly void BackupEdgeNormals(Vector3[] edge, int vCount, int localStart, int localDelta)
    {
        var buildQuad = pq.Target;

        int num = localStart;
        for (int i = 0; i < vCount; i++)
        {
            edge[i] = buildQuad.vertNormals[num];
            num += localDelta;
        }
    }

    readonly int vi(int x, int z)
    {
        return z * cacheSideVertCount + x;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowArraySizeMismatch() =>
        throw new IndexOutOfRangeException("CopyToNativeArray: array sizes did not match");

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowMismatchedVertexCount() =>
        throw new Exception("InitMeshData: side vertex count does not match total vertex count");

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowMismatchedCacheVerts() =>
        throw new Exception("CacheVerts length was not equal to data vertex count");
}

[BurstCompile]
internal static unsafe class BuildQuadJobExt
{
    delegate void InitHeightDataDelegate(BuildQuadJob* job, BuildHeightsData* data);
    delegate void InitVertexDataDelegate(BuildQuadJob* job, BuildVerticesData* data);
    delegate void InitMeshDataDelegate(
        BuildQuadJob* job,
        BuildMeshData* data,
        NativeArray<Vector3>* tan2,
        NativeArray<int>* indices
    );
    delegate void BuildMeshDelegate(BuildQuadJob* job, BuildMeshData* data, MeshDataStruct* mesh);

    static readonly InitHeightDataDelegate InitHeightDataFunc;
    static readonly InitVertexDataDelegate InitVertexDataFunc;
    static readonly InitMeshDataDelegate InitMeshDataFunc;
    static readonly BuildMeshDelegate BuildMeshFunc;

    static BuildQuadJobExt()
    {
        InitHeightDataFunc = BurstUtil
            .MaybeCompileFunctionPointer<InitHeightDataDelegate>(InitHeightDataBurst)
            .Invoke;

        InitVertexDataFunc = BurstUtil
            .MaybeCompileFunctionPointer<InitVertexDataDelegate>(InitVertexDataBurst)
            .Invoke;

        InitMeshDataFunc = BurstUtil
            .MaybeCompileFunctionPointer<InitMeshDataDelegate>(InitMeshDataBurst)
            .Invoke;

        BuildMeshFunc = BurstUtil
            .MaybeCompileFunctionPointer<BuildMeshDelegate>(BuildMeshBurst)
            .Invoke;
    }

    // We need to use the Unsafe.AsRef/fixed dance below as otherwise the proper address
    // can change out from underneath us before the method gets a chance to run.

    internal static void InitHeightData(this ref BuildQuadJob job, ref BuildHeightsData data)
    {
        fixed (BuildQuadJob* pjob = &job)
        fixed (BuildHeightsData* pdata = &data)
        {
            InitHeightDataFunc(pjob, pdata);
        }
    }

    internal static void InitVertexData(this ref BuildQuadJob job, ref BuildVerticesData data)
    {
        fixed (BuildQuadJob* pjob = &job)
        fixed (BuildVerticesData* pdata = &data)
        {
            InitVertexDataFunc(pjob, pdata);
        }
    }

    internal static void InitMeshData(
        this ref BuildQuadJob job,
        ref BuildMeshData data,
        NativeArray<Vector3> tan2,
        NativeArray<int> indices
    )
    {
        fixed (BuildQuadJob* pjob = &job)
        fixed (BuildMeshData* pdata = &data)
        {
            InitMeshDataFunc(pjob, pdata, &tan2, &indices);
        }
    }

    internal static void BuildMesh(
        this ref BuildQuadJob job,
        ref BuildMeshData data,
        ref MeshDataStruct mesh
    )
    {
        fixed (BuildQuadJob* pjob = &job)
        fixed (BuildMeshData* pdata = &data)
        fixed (MeshDataStruct* pmesh = &mesh)
        {
            BuildMeshFunc(pjob, pdata, pmesh);
        }
    }

    [BurstCompile]
    static void InitHeightDataBurst(BuildQuadJob* job, BuildHeightsData* data) =>
        Unsafe
            .AsRef<BuildQuadJob>(job)
            .InitHeightDataImpl(ref Unsafe.AsRef<BuildHeightsData>(data));

    [BurstCompile]
    static void InitVertexDataBurst(BuildQuadJob* job, BuildVerticesData* data) =>
        Unsafe
            .AsRef<BuildQuadJob>(job)
            .InitVertexDataImpl(ref Unsafe.AsRef<BuildVerticesData>(data));

    [BurstCompile]
    static void InitMeshDataBurst(
        BuildQuadJob* job,
        BuildMeshData* data,
        NativeArray<Vector3>* tan2,
        NativeArray<int>* indices
    ) =>
        Unsafe
            .AsRef<BuildQuadJob>(job)
            .InitMeshDataImpl(ref Unsafe.AsRef<BuildMeshData>(data), *tan2, *indices);

    [BurstCompile]
    static void BuildMeshBurst(BuildQuadJob* job, BuildMeshData* data, MeshDataStruct* mesh) =>
        Unsafe
            .AsRef<BuildQuadJob>(job)
            .BuildMeshImpl(
                ref Unsafe.AsRef<BuildMeshData>(data),
                ref Unsafe.AsRef<MeshDataStruct>(mesh)
            );

    // This is a no-op, it just makes sure that the static ctor is called on the main thread.
    internal static void Init() { }
}
