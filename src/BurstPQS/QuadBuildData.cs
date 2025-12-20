using System;
using System.Runtime.CompilerServices;
using BurstPQS.Collections;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

#pragma warning disable IDE1006 // Naming Styles

namespace BurstPQS;

public class QuadBuildData : IDisposable
{
    public readonly PQ buildQuad;
    public readonly BurstQuadBuildData burst;

    public int VertexCount => burst.VertexCount;
    public MemorySpan<Vector3d> globalV => burst.globalV;
    public MemorySpan<Vector3d> directionFromCenter => burst.directionFromCenter;
    public MemorySpan<Vector3d> directionD => burst.directionD;
    public MemorySpan<Vector3d> directionXZ => burst.directionXZ;
    public MemorySpan<double> vertHeight => burst.vertHeight;
    public MemorySpan<Color> vertColor => burst.vertColor;
    public MemorySpan<double> u => burst.u;
    public MemorySpan<double> v => burst.v;
    public MemorySpan<double> u2 => burst.u2;
    public MemorySpan<double> v2 => burst.v2;
    public MemorySpan<double> u3 => burst.u3;
    public MemorySpan<double> v3 => burst.v3;
    public MemorySpan<double> u4 => burst.u4;
    public MemorySpan<double> v4 => burst.v4;
    public MemorySpan<double> gnomonicU => burst.gnomonicU;
    public MemorySpan<double> gnomonicV => burst.gnomonicV;
    public MemorySpan<bool> allowScatter => burst.allowScatter;
    public MemorySpan<double> longitude => burst.longitude;
    public MemorySpan<double> latitude => burst.latitude;
    public MemorySpan<FixedArray6<PQS.GnomonicUV>> gnomonicUVs => burst.gnomonicUVs;

    public BurstQuadBuildData.SX sx => burst.sx;
    public BurstQuadBuildData.SY sy => burst.sy;

    public QuadBuildData(PQS sphere, int vertexCount)
    {
        buildQuad = sphere.buildQuad;
        burst = new(sphere, vertexCount);
    }

    public void CopyTo(PQS.VertexBuildData vbdata, int index)
    {
        if ((uint)index >= (uint)VertexCount)
            throw new IndexOutOfRangeException();

        vbdata.vertIndex = index;

        vbdata.globalV = globalV[index];
        vbdata.directionFromCenter = directionFromCenter[index];
        vbdata.directionD = directionD[index];
        vbdata.directionXZ = directionXZ[index];
        vbdata.vertHeight = vertHeight[index];
        vbdata.vertColor = vertColor[index];
        vbdata.u = u[index];
        vbdata.v = v[index];
        vbdata.u2 = u2[index];
        vbdata.v2 = v2[index];
        vbdata.u3 = u3[index];
        vbdata.v3 = v3[index];
        vbdata.u4 = u4[index];
        vbdata.v4 = v4[index];
        vbdata.gnomonicU = gnomonicU[index];
        vbdata.gnomonicV = gnomonicV[index];
        vbdata.allowScatter = allowScatter[index];
        vbdata.longitude = longitude[index];
        vbdata.latitude = latitude[index];

        for (int i = 0; i < 6; ++i)
            vbdata.gnomonicUVs[i] = gnomonicUVs[index][i];
    }

    public void CopyFrom(PQS.VertexBuildData vbdata, int index)
    {
        if ((uint)index >= (uint)VertexCount)
            throw new IndexOutOfRangeException();

        globalV[index] = vbdata.globalV;
        directionFromCenter[index] = vbdata.directionFromCenter;
        directionD[index] = vbdata.directionD;
        directionXZ[index] = vbdata.directionXZ;
        vertHeight[index] = vbdata.vertHeight;
        vertColor[index] = vbdata.vertColor;
        u[index] = vbdata.u;
        v[index] = vbdata.v;
        u2[index] = vbdata.u2;
        v2[index] = vbdata.v2;
        u3[index] = vbdata.u3;
        v3[index] = vbdata.v3;
        u4[index] = vbdata.u4;
        v4[index] = vbdata.v4;
        gnomonicU[index] = vbdata.gnomonicU;
        gnomonicV[index] = vbdata.gnomonicV;
        allowScatter[index] = vbdata.allowScatter;
        longitude[index] = vbdata.longitude;
        latitude[index] = vbdata.latitude;

        for (int i = 0; i < 6; ++i)
            gnomonicUVs[index][i] = vbdata.gnomonicUVs[i];
    }

    public void Dispose()
    {
        burst.Dispose();
    }
}

public unsafe struct BurstQuadBuildData : IDisposable
{
    #region Fields
    int _vertexCount;

    [NoAlias]
    void* _data;

    #endregion

    public readonly int VertexCount
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => _vertexCount;
    }

    #region Public Fields
    public struct Sphere
    {
        public double radius;
        public double radiusMin;
        public double radiusMax;

        public readonly double radiusDelta => radiusMax - radiusMin;
    }

    public Sphere sphere { get; private set; }
    public PQS.QuadPlane plane { get; private set; }
    #endregion

    #region Per-Element Offsets
    // sizeof(Vector3d) is not a C# constant, even if it is constant in practice.
    const int SizeofVector3 = 3 * sizeof(double);
    const int SizeofColor = 4 * sizeof(float);

    const int EndOff_GlobalV = SizeofVector3;
    const int EndOff_DirectionFromCenter = SizeofVector3 + EndOff_GlobalV;
    const int EndOff_DirectionD = SizeofVector3 + EndOff_DirectionFromCenter;
    const int EndOff_DirectionXZ = SizeofVector3 + EndOff_DirectionD;
    const int EndOff_VertHeight = sizeof(double) + EndOff_DirectionXZ;
    const int EndOff_VertColor = SizeofColor + EndOff_VertHeight;
    const int EndOff_U = sizeof(double) + EndOff_VertColor;
    const int EndOff_V = sizeof(double) + EndOff_U;
    const int EndOff_U2 = sizeof(double) + EndOff_V;
    const int EndOff_V2 = sizeof(double) + EndOff_U2;
    const int EndOff_U3 = sizeof(double) + EndOff_V2;
    const int EndOff_V3 = sizeof(double) + EndOff_U3;
    const int EndOff_U4 = sizeof(double) + EndOff_V3;
    const int EndOff_V4 = sizeof(double) + EndOff_U4;
    const int EndOff_GnomonicU = sizeof(double) + EndOff_V4;
    const int EndOff_GnomonicV = sizeof(double) + EndOff_GnomonicU;
    const int EndOff_Longitude = sizeof(double) + EndOff_GnomonicV;
    const int EndOff_Latitude = sizeof(double) + EndOff_Longitude;
    static int EndOff_GnomonicUVs => sizeof(FixedArray6<PQS.GnomonicUV>) + EndOff_Latitude;
    static int EndOff_AllowScatter => sizeof(bool) + EndOff_GnomonicUVs;

    static int TotalElemSize => EndOff_AllowScatter;
    #endregion

    #region Arrays
    public static int GetTotalAllocationSize(int vertexCount) => vertexCount * EndOff_AllowScatter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly MemorySpan<T> GetOffsetSlice<T>(int endOff)
        where T : unmanaged
    {
        return new((T*)((byte*)_data + (endOff - sizeof(T)) * VertexCount), VertexCount);
    }

    public readonly MemorySpan<Vector3d> globalV => GetOffsetSlice<Vector3d>(EndOff_GlobalV);
    public readonly MemorySpan<Vector3d> directionFromCenter =>
        GetOffsetSlice<Vector3d>(EndOff_DirectionFromCenter);
    public readonly MemorySpan<Vector3d> directionD => GetOffsetSlice<Vector3d>(EndOff_DirectionD);
    public readonly MemorySpan<Vector3d> directionXZ =>
        GetOffsetSlice<Vector3d>(EndOff_DirectionXZ);
    public readonly MemorySpan<double> vertHeight => GetOffsetSlice<double>(EndOff_VertHeight);
    public readonly MemorySpan<Color> vertColor => GetOffsetSlice<Color>(EndOff_VertColor);
    public readonly MemorySpan<double> u => GetOffsetSlice<double>(EndOff_U);
    public readonly MemorySpan<double> v => GetOffsetSlice<double>(EndOff_V);
    public readonly MemorySpan<double> u2 => GetOffsetSlice<double>(EndOff_U2);
    public readonly MemorySpan<double> v2 => GetOffsetSlice<double>(EndOff_V2);
    public readonly MemorySpan<double> u3 => GetOffsetSlice<double>(EndOff_U3);
    public readonly MemorySpan<double> v3 => GetOffsetSlice<double>(EndOff_V3);
    public readonly MemorySpan<double> u4 => GetOffsetSlice<double>(EndOff_U4);
    public readonly MemorySpan<double> v4 => GetOffsetSlice<double>(EndOff_V4);
    public readonly MemorySpan<double> gnomonicU => GetOffsetSlice<double>(EndOff_GnomonicU);
    public readonly MemorySpan<double> gnomonicV => GetOffsetSlice<double>(EndOff_GnomonicV);
    public readonly MemorySpan<bool> allowScatter => GetOffsetSlice<bool>(EndOff_AllowScatter);
    public readonly MemorySpan<double> longitude => GetOffsetSlice<double>(EndOff_Longitude);
    public readonly MemorySpan<double> latitude => GetOffsetSlice<double>(EndOff_Latitude);
    public readonly MemorySpan<FixedArray6<PQS.GnomonicUV>> gnomonicUVs =>
        GetOffsetSlice<FixedArray6<PQS.GnomonicUV>>(EndOff_GnomonicUVs);

    public readonly SX sx => new(in this);
    public readonly SY sy => new(in this);
    #endregion

    public BurstQuadBuildData(PQS sphere, int vertexCount)
    {
        if (sphere is null)
            throw new ArgumentNullException(nameof(sphere));

        this.sphere = new Sphere
        {
            radius = sphere.radius,
            radiusMin = sphere.radiusMin,
            radiusMax = sphere.radiusMax,
        };
        plane = sphere.buildQuad.plane;

        var size = TotalElemSize * vertexCount;
        var data = UnsafeUtility.Malloc(size, sizeof(double), Allocator.TempJob);
        if (data is null)
            throw new Exception("malloc returned null");

        UnsafeUtility.MemClear(data, size);

        _data = data;
        _vertexCount = vertexCount;
    }

    public void Dispose()
    {
        UnsafeUtility.Free(_data, Allocator.TempJob);
        _data = null;
    }

    #region Access Structs

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly struct SY(in BurstQuadBuildData data)
    {
        readonly MemorySpan<double> latitude = data.latitude;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => latitude.Length;
        }
        public double this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => latitude[index] / Math.PI + 0.5;
        }
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly struct SX(in BurstQuadBuildData data)
    {
        readonly MemorySpan<double> longitude = data.longitude;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => longitude.Length;
        }
        public double this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => longitude[index] / Math.PI * 0.5;
        }
    }
    #endregion
}
