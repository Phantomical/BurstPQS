using System;
using System.Runtime.CompilerServices;
using BurstPQS.Collections;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS;

public struct SphereData(PQS sphere)
{
    public double radius = sphere.radius;
    public double radiusMin = sphere.radiusMin;
    public double radiusMax = sphere.radiusMax;
    public bool isBuildingMaps = sphere.isBuildingMaps;

    public readonly double radiusDelta => radiusMax - radiusMin;
}

public unsafe struct BuildHeightsData
{
    public SphereData sphere { get; private set; }

    public int VertexCount
    {
        [return: AssumeRange(0, int.MaxValue)]
        get;
        internal set;
    }

    [NoAlias]
    internal Vector3d* _directionFromCenter;

    [NoAlias]
    readonly double* _u;

    [NoAlias]
    readonly double* _v;

    [NoAlias]
    readonly double* _sx;

    [NoAlias]
    readonly double* _sy;

    [NoAlias]
    readonly double* _longitude;

    [NoAlias]
    readonly double* _latitude;

    [NoAlias]
    readonly double* _height;

    [NoAlias]
    internal Color* _vertColor;

    internal BuildHeightsData(SphereData sphere, int vertexCount)
    {
        this.sphere = sphere;
        VertexCount = vertexCount;

        _directionFromCenter = AllocVertexArray<Vector3d>();
        _u = AllocVertexArray<double>();
        _v = AllocVertexArray<double>();
        _sx = AllocVertexArray<double>();
        _sy = AllocVertexArray<double>();
        _longitude = AllocVertexArray<double>();
        _latitude = AllocVertexArray<double>();
        _height = AllocVertexArray<double>();
        _vertColor = AllocVertexArray<Color>();
    }

    internal readonly T* AllocVertexArray<T>()
        where T : unmanaged
    {
        var ptr = (T*)
            UnsafeUtility.Malloc(
                VertexCount * sizeof(T),
                UnsafeUtility.AlignOf<T>(),
                Allocator.Temp
            );
        if (ptr is null)
            throw new OutOfMemoryException("failed to allocate PQS temporary buffer data");
        return ptr;
    }

    public readonly MemorySpan<Vector3d> directionFromCenter =>
        CreateNativeArray(_directionFromCenter);

    public readonly MemorySpan<double> vertHeight => CreateNativeArray(_height);
    public readonly MemorySpan<Color> vertColor => CreateNativeArray(_vertColor);

    public readonly MemorySpan<double> u => CreateNativeArray(_u);
    public readonly MemorySpan<double> v => CreateNativeArray(_v);

    public readonly MemorySpan<double> sx => CreateNativeArray(_sx);
    public readonly MemorySpan<double> sy => CreateNativeArray(_sy);

    public readonly MemorySpan<double> longitude => CreateNativeArray(_longitude);
    public readonly MemorySpan<double> latitude => CreateNativeArray(_latitude);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly MemorySpan<T> CreateNativeArray<T>(T* data)
        where T : unmanaged
    {
        return new MemorySpan<T>(data, VertexCount);
    }
}

public unsafe struct BuildVerticesData
{
    internal BuildHeightsData data;

    public readonly SphereData sphere => data.sphere;

    public readonly int VertexCount => data.VertexCount;

    #region BuildHeightData
    public readonly MemorySpan<Vector3d> directionFromCenter => data.directionFromCenter;

    public readonly MemorySpan<double> vertHeight => data.vertHeight;
    public readonly MemorySpan<Color> vertColor => data.vertColor;

    public readonly MemorySpan<double> u => data.u;
    public readonly MemorySpan<double> v => data.v;

    public readonly MemorySpan<double> sx => data.sx;
    public readonly MemorySpan<double> sy => data.sy;

    public readonly MemorySpan<double> longitude => data.longitude;
    public readonly MemorySpan<double> latitude => data.latitude;
    #endregion

    #region BuildVerticesData
    [NoAlias]
    internal double* _u2;

    [NoAlias]
    internal double* _v2;

    [NoAlias]
    internal double* _u3;

    [NoAlias]
    internal double* _v3;

    [NoAlias]
    internal double* _u4;

    [NoAlias]
    internal double* _v4;

    [NoAlias]
    internal bool* _allowScatter;

    internal BuildVerticesData(BuildHeightsData data)
    {
        this.data = data;

        _u2 = AllocVertexArray<double>();
        _v2 = AllocVertexArray<double>();
        _u3 = AllocVertexArray<double>();
        _v3 = AllocVertexArray<double>();
        _u4 = AllocVertexArray<double>();
        _v4 = AllocVertexArray<double>();
        _allowScatter = AllocVertexArray<bool>();
    }

    internal readonly T* AllocVertexArray<T>()
        where T : unmanaged
    {
        return data.AllocVertexArray<T>();
    }

    public readonly MemorySpan<double> u2 => CreateNativeArray(_u2);
    public readonly MemorySpan<double> u3 => CreateNativeArray(_u3);
    public readonly MemorySpan<double> u4 => CreateNativeArray(_u4);
    public readonly MemorySpan<double> v2 => CreateNativeArray(_v2);
    public readonly MemorySpan<double> v3 => CreateNativeArray(_v3);
    public readonly MemorySpan<double> v4 => CreateNativeArray(_v4);
    public readonly MemorySpan<bool> allowScatter => CreateNativeArray(_allowScatter);
    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly MemorySpan<T> CreateNativeArray<T>(T* data)
        where T : unmanaged
    {
        return new MemorySpan<T>(data, VertexCount);
    }
}

public unsafe struct BuildMeshData
{
    internal BuildVerticesData data;

    public readonly SphereData sphere => data.sphere;

    public readonly int VertexCount => data.VertexCount;

    public double VertMax { get; internal set; }
    public double VertMin { get; internal set; }

    #region BuildHeightData
    public readonly MemorySpan<Vector3d> directionFromCenter => data.directionFromCenter;

    public readonly MemorySpan<double> vertHeight => data.vertHeight;
    public readonly MemorySpan<Color> vertColor => data.vertColor;

    public readonly MemorySpan<double> u => data.u;
    public readonly MemorySpan<double> v => data.v;

    public readonly MemorySpan<double> sx => data.sx;
    public readonly MemorySpan<double> sy => data.sy;

    public readonly MemorySpan<double> longitude => data.longitude;
    public readonly MemorySpan<double> latitude => data.latitude;
    #endregion

    #region BuildVerticesData
    public readonly MemorySpan<double> u2 => data.u2;
    public readonly MemorySpan<double> u3 => data.u3;
    public readonly MemorySpan<double> u4 => data.u4;
    public readonly MemorySpan<double> v2 => data.v2;
    public readonly MemorySpan<double> v3 => data.v3;
    public readonly MemorySpan<double> v4 => data.v4;
    public readonly MemorySpan<bool> allowScatter => data.allowScatter;
    #endregion

    readonly Vector3d* _vertsD;
    readonly Vector3* _verts;
    readonly Vector3* _normals;
    readonly Vector2* _uvs;
    readonly Vector2* _uv2s;
    readonly Vector2* _uv3s;
    readonly Vector2* _uv4s;
    readonly Vector4* _tangents;

    public readonly MemorySpan<Vector3d> vertsD => CreateNativeArray(_vertsD);
    public readonly MemorySpan<Vector3> verts => CreateNativeArray(_verts);
    public readonly MemorySpan<Vector3> normals => CreateNativeArray(_normals);
    public readonly MemorySpan<Vector2> uvs => CreateNativeArray(_uvs);
    public readonly MemorySpan<Vector2> uv2s => CreateNativeArray(_uv2s);
    public readonly MemorySpan<Vector2> uv3s => CreateNativeArray(_uv3s);
    public readonly MemorySpan<Vector2> uv4s => CreateNativeArray(_uv4s);
    public readonly MemorySpan<Vector4> tangents => CreateNativeArray(_tangents);

    internal BuildMeshData(BuildVerticesData data)
    {
        this.data = data;

        _vertsD = AllocVertexArray<Vector3d>();
        _verts = AllocVertexArray<Vector3>();
        _normals = AllocVertexArray<Vector3>();
        _uvs = AllocVertexArray<Vector2>();
        _uv2s = AllocVertexArray<Vector2>();
        _uv3s = AllocVertexArray<Vector2>();
        _uv4s = AllocVertexArray<Vector2>();
        _tangents = AllocVertexArray<Vector4>();
    }

    internal readonly T* AllocVertexArray<T>()
        where T : unmanaged
    {
        return data.AllocVertexArray<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly MemorySpan<T> CreateNativeArray<T>(T* data)
        where T : unmanaged
    {
        return new MemorySpan<T>(data, VertexCount);
    }
}
