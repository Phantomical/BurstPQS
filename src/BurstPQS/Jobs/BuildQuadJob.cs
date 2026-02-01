using System;
using System.Collections.Generic;
using System.Linq;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static PQ;

namespace BurstPQS.Jobs;

internal struct BuildQuadJob : IJob
{
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
    public int cacheMeshSize;
    public int cacheRes;
    public int cacheTriCount;

    public SphereData sphere;
    public double meshVertMax;
    public double meshVertMin;

    public ObjectHandle<BatchPQSJobSet> jobSet;
    public ObjectHandle<MeshData> meshData;
    public ObjectHandle<PQ> pq;

    public unsafe void Execute()
    {
        using var jsguard = this.jobSet;
        using var mdguard = this.meshData;
        using var pqguard = this.pq;

        var jobSet = this.jobSet.Target;
        var meshOutputData = this.meshData.Target;
        var pq = this.pq.Target;

        var heightData = new BuildHeightsData(sphere, cacheVertexCount);
        this.InitHeightData(in heightData);
        jobSet.BuildHeights(in heightData);

        var vertexData = new BuildVerticesData(heightData);
        this.InitVertexData(in vertexData);
        jobSet.BuildVertices(in vertexData);

        var meshData = new BuildMeshData(vertexData);
        this.InitMeshData(ref meshData);
        jobSet.BuildMesh(in meshData);

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

            if (reqCustomNormals)
                BuildMeshCustomNormals(in meshData, indices);

            if (reqBuildTangents)
                BuildMeshTangents(in meshData, tan2);
        }

        BuildMesh(ref meshData, meshOutputData);
    }

    internal readonly void InitHeightDataImpl(in BuildHeightsData data)
    {
        if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
            throw new Exception("CacheVerts length was not equal to data vertex count");

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
            data.sx[i] = data.v[i];
            data.sy[i] = data.u[i];
        }
    }

    internal readonly void InitVertexDataImpl(in BuildVerticesData data)
    {
        data.vertColor.Clear();
        data.u2.Clear();
        data.v2.Clear();
        data.u3.Clear();
        data.v3.Clear();
        data.u4.Clear();
        data.v4.Clear();
    }

    internal readonly void InitMeshDataImpl(ref BuildMeshData data)
    {
        if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
            throw new Exception("side vertex count doesn't match total vertex count");

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

        var vertMax = double.MinValue;
        var vertMin = double.MaxValue;

        foreach (var vheight in data.vertHeight)
        {
            var height = vheight - data.sphere.radius;
            height = MathUtil.Clamp(height, meshVertMin, meshVertMax);

            vertMax = Math.Max(vertMax, height);
            vertMin = Math.Min(vertMin, height);
        }

        data.VertMax = vertMax;
        data.VertMin = vertMin;
    }

    internal readonly void BuildMesh(ref BuildMeshData data, MeshData mesh)
    {
        var descs = new List<VertexAttributeDescriptor>()
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        };

        if (reqSphereUV || reqUVQuad)
            descs.Add(new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
        if (reqUV2)
            descs.Add(new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2));
        if (reqUV3)
            descs.Add(new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2));
        if (reqUV4)
            descs.Add(new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2));
        if (reqColorChannel)
            descs.Add(new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4));
        if (reqAssignTangents)
            descs.Add(new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4));

        var stride = descs.Sum(attr => attr.dimension * sizeof(float));
        var meshData = new NativeArray<byte>(data.VertexCount * stride, Allocator.TempJob);
        mesh.vertexData = meshData;

        AssignMeshData(meshData, descs, VertexAttribute.Position, data.verts);

        if (reqSphereUV || reqUVQuad)
            AssignMeshData(meshData, descs, VertexAttribute.TexCoord0, data.uvs);
        if (reqUV2)
            AssignMeshData(meshData, descs, VertexAttribute.TexCoord1, data.uv2s);
        if (reqUV3)
            AssignMeshData(meshData, descs, VertexAttribute.TexCoord2, data.uv3s);
        if (reqUV4)
            AssignMeshData(meshData, descs, VertexAttribute.TexCoord3, data.uv4s);

        if (reqColorChannel)
            AssignMeshData(meshData, descs, VertexAttribute.Color, data.vertColor);

        var normals = new NativeArray<Vector3>(data.VertexCount, Allocator.TempJob);
        mesh.normalData = normals;
        normals.CopyFrom(data.normals.AsNativeArray());
        descs.Add(new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1));

        if (reqAssignTangents)
        {
            var tangents = new NativeArray<Vector4>(data.VertexCount, Allocator.TempJob);
            mesh.tangentData = tangents;
            tangents.CopyFrom(data.tangents.AsNativeArray());
            descs.Add(new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 2));
        }

        mesh.descriptors = [.. descs];
    }

    readonly unsafe void AssignMeshData<T>(
        NativeArray<byte> vertexBuffer,
        List<VertexAttributeDescriptor> descs,
        VertexAttribute attribute,
        MemorySpan<T> data
    )
        where T : unmanaged
    {
        var stride = descs.Sum(attr => attr.dimension * sizeof(float));
        var offset = descs
            .TakeWhile(attr => attr.attribute != attribute)
            .Sum(attr => attr.dimension * sizeof(float));

        var slice = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<T>(
            vertexBuffer.GetUnsafePtr(),
            stride,
            cacheVertexCount
        );

        for (int i = 0; i < cacheVertexCount; ++i)
            slice[i] = data[i];
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
                (float)(data.latitude[i] / Math.PI + 0.5),
                (float)(data.longitude[i] / Math.PI * 0.5)
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

    readonly void BuildMeshCustomNormals(in BuildMeshData data, NativeArray<int> indices)
    {
        BuildMeshNormals(in data, indices);
        BackupEdgeNormals();
    }

    readonly void BuildMeshTangents(in BuildMeshData data, NativeArray<Vector3> tan2)
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var normal = data.normals[i];
            var tangent = Vector3.zero;
            Vector3.OrthoNormalize(ref normal, ref tangent);

            data.tangents[i] = new Vector4(
                tangent.x,
                tangent.y,
                tangent.z,
                (Vector3.Dot(Vector3.Cross(normal, tangent), tan2[i]) < 0f) ? -1f : 1f
            );
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
            var ba = data.verts[indices[j + 1]] - data.verts[indices[j]];
            var ca = data.verts[indices[j + 2]] - data.verts[indices[j]];

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
}

[BurstCompile]
internal static class BuildQuadJobExt
{
    delegate void InitHeightDataDelegate(ref BuildQuadJob job, in BuildHeightsData data);
    delegate void InitVertexDataDelegate(ref BuildQuadJob job, in BuildVerticesData data);
    delegate void InitMeshDataDelegate(ref BuildQuadJob job, ref BuildMeshData data);

    static readonly InitHeightDataDelegate InitHeightDataFunc;
    static readonly InitVertexDataDelegate InitVertexDataFunc;
    static readonly InitMeshDataDelegate InitMeshDataFunc;

    static BuildQuadJobExt()
    {
        InitHeightDataFunc = BurstCompiler
            .CompileFunctionPointer<InitHeightDataDelegate>(InitHeightDataBurst)
            .Invoke;

        InitVertexDataFunc = BurstCompiler
            .CompileFunctionPointer<InitVertexDataDelegate>(InitVertexDataBurst)
            .Invoke;

        InitMeshDataFunc = BurstCompiler
            .CompileFunctionPointer<InitMeshDataDelegate>(InitMeshDataBurst)
            .Invoke;
    }

    internal static void InitHeightData(this ref BuildQuadJob job, in BuildHeightsData data) =>
        InitHeightDataFunc(ref job, in data);

    internal static void InitVertexData(this ref BuildQuadJob job, in BuildVerticesData data) =>
        InitVertexDataFunc(ref job, in data);

    internal static void InitMeshData(this ref BuildQuadJob job, ref BuildMeshData data) =>
        InitMeshDataFunc(ref job, ref data);

    [BurstCompile]
    static void InitHeightDataBurst(this ref BuildQuadJob job, in BuildHeightsData data) =>
        job.InitHeightDataImpl(in data);

    [BurstCompile]
    static void InitVertexDataBurst(this ref BuildQuadJob job, in BuildVerticesData data) =>
        job.InitVertexDataImpl(in data);

    [BurstCompile]
    static void InitMeshDataBurst(this ref BuildQuadJob job, ref BuildMeshData data) =>
        job.InitMeshDataImpl(ref data);
}
