using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BurstPQS.Collections;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using UnityEngine;
using VehiclePhysics;

#pragma warning disable IDE1006 // Naming Styles

namespace BurstPQS;

public struct QuadBuildData
{
    public PQ buildQuad;
    public BurstQuadBuildData burstData;

    public readonly int VertexCount => burstData.VertexCount;
    public readonly MemorySpan<Vector3d> globalV => burstData.globalV;
    public readonly MemorySpan<Vector3d> directionFromCenter => burstData.directionFromCenter;
    public readonly MemorySpan<Vector3d> directionD => burstData.directionD;
    public readonly MemorySpan<Vector3d> directionXZ => burstData.directionXZ;
    public readonly MemorySpan<double> vertHeight => burstData.vertHeight;
    public readonly MemorySpan<Color> vertColor => burstData.vertColor;
    public readonly MemorySpan<double> u => burstData.u;
    public readonly MemorySpan<double> v => burstData.v;
    public readonly MemorySpan<double> u2 => burstData.u2;
    public readonly MemorySpan<double> v2 => burstData.v2;
    public readonly MemorySpan<double> u3 => burstData.u3;
    public readonly MemorySpan<double> v3 => burstData.v3;
    public readonly MemorySpan<double> u4 => burstData.u4;
    public readonly MemorySpan<double> v4 => burstData.v4;
    public readonly MemorySpan<double> gnomonicU => burstData.gnomonicU;
    public readonly MemorySpan<double> gnomonicV => burstData.gnomonicV;
    public readonly MemorySpan<bool> allowScatter => burstData.allowScatter;
    public readonly MemorySpan<double> longitude => burstData.longitude;
    public readonly MemorySpan<double> latitude => burstData.latitude;
    public readonly MemorySpan<FixedArray6<PQS.GnomonicUV>> gnomonicUVs => burstData.gnomonicUVs;

    public readonly BurstQuadBuildData.SX sx => burstData.sx;
    public readonly BurstQuadBuildData.SY sy => burstData.sy;

    public readonly void CopyTo(PQS.VertexBuildData vbdata, int index)
    {
        burstData.CopyTo(vbdata, index);

        vbdata.buildQuad = buildQuad;
    }

    public readonly void CopyFrom(PQS.VertexBuildData vbdata, int index) =>
        burstData.CopyFrom(vbdata, index);
}

public unsafe struct BurstQuadBuildData
{
    readonly int _vertexCount;
    readonly int _baseIndex;

    [NoAlias]
    readonly Vector3d* _globalV;

    [NoAlias]
    readonly Vector3d* _directionFromCenter;

    [NoAlias]
    readonly Vector3d* _directionD;

    [NoAlias]
    readonly Vector3d* _directionXZ;

    [NoAlias]
    readonly double* _vertHeight;

    [NoAlias]
    readonly Color* _vertColor;

    [NoAlias]
    readonly double* _u;

    [NoAlias]
    readonly double* _v;

    [NoAlias]
    readonly double* _u2;

    [NoAlias]
    readonly double* _v2;

    [NoAlias]
    readonly double* _u3;

    [NoAlias]
    readonly double* _v3;

    [NoAlias]
    readonly double* _u4;

    [NoAlias]
    readonly double* _v4;

    [NoAlias]
    readonly double* _gnomonicU;

    [NoAlias]
    readonly double* _gnomonicV;

    [NoAlias]
    readonly bool* _allowScatter;

    [NoAlias]
    readonly double* _longitude;

    [NoAlias]
    readonly double* _latitude;

    [NoAlias]
    readonly FixedArray6<PQS.GnomonicUV>* _gnomonicUVs;

    public PQS.QuadPlane gnomonicPlane;

    public readonly int VertexCount
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_vertexCount < 0)
                Hint.Assume(false);
            return _vertexCount;
        }
    }

    public readonly VertexIndices vertIndex => new(_baseIndex, VertexCount);
    public readonly MemorySpan<Vector3d> globalV => new(_globalV, VertexCount);
    public readonly MemorySpan<Vector3d> directionFromCenter =>
        new(_directionFromCenter, VertexCount);
    public readonly MemorySpan<Vector3d> directionD => new(_directionD, VertexCount);
    public readonly MemorySpan<Vector3d> directionXZ => new(_directionXZ, VertexCount);
    public readonly MemorySpan<double> vertHeight => new(_vertHeight, VertexCount);
    public readonly MemorySpan<Color> vertColor => new(_vertColor, VertexCount);
    public readonly MemorySpan<double> u => new(_u, VertexCount);
    public readonly MemorySpan<double> v => new(_v, VertexCount);
    public readonly MemorySpan<double> u2 => new(_u2, VertexCount);
    public readonly MemorySpan<double> v2 => new(_v2, VertexCount);
    public readonly MemorySpan<double> u3 => new(_u3, VertexCount);
    public readonly MemorySpan<double> v3 => new(_v3, VertexCount);
    public readonly MemorySpan<double> u4 => new(_u4, VertexCount);
    public readonly MemorySpan<double> v4 => new(_v4, VertexCount);
    public readonly MemorySpan<double> gnomonicU => new(_gnomonicU, VertexCount);
    public readonly MemorySpan<double> gnomonicV => new(_gnomonicV, VertexCount);
    public readonly MemorySpan<bool> allowScatter => new(_allowScatter, VertexCount);
    public readonly MemorySpan<double> longitude => new(_longitude, VertexCount);
    public readonly MemorySpan<double> latitude => new(_latitude, VertexCount);
    public readonly MemorySpan<FixedArray6<PQS.GnomonicUV>> gnomonicUVs =>
        new(_gnomonicUVs, VertexCount);

    public readonly SX sx => new(in this);
    public readonly SY sy => new(in this);

    internal BurstQuadBuildData(
        PQ quad,
        void* buffer,
        int buflen,
        int vertexCount,
        int baseIndex = 0
    )
    {
        if (vertexCount < 0)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (buflen < 0)
            throw new ArgumentOutOfRangeException(nameof(buflen));
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        var p = buffer;
        var remaining = buflen;

        T* TakeElements<T>()
            where T : unmanaged
        {
            int requested = sizeof(T) * vertexCount;
            if (requested > remaining)
                throw new InvalidOperationException(
                    "not enough buffer space to store all vertex data"
                );

            T* result = (T*)p;
            p = (byte*)p + requested;
            remaining -= requested;
            return result;
        }

        gnomonicPlane = quad.plane;
        _baseIndex = baseIndex;
        _vertexCount = vertexCount;
        _globalV = TakeElements<Vector3d>();
        _directionFromCenter = TakeElements<Vector3d>();
        _directionD = TakeElements<Vector3d>();
        _directionXZ = TakeElements<Vector3d>();
        _vertHeight = TakeElements<double>();
        _vertColor = TakeElements<Color>();
        _u = TakeElements<double>();
        _v = TakeElements<double>();
        _u2 = TakeElements<double>();
        _v2 = TakeElements<double>();
        _u3 = TakeElements<double>();
        _v3 = TakeElements<double>();
        _u4 = TakeElements<double>();
        _v4 = TakeElements<double>();
        _gnomonicU = TakeElements<double>();
        _gnomonicV = TakeElements<double>();
        _longitude = TakeElements<double>();
        _latitude = TakeElements<double>();
        _gnomonicUVs = TakeElements<FixedArray6<PQS.GnomonicUV>>();

        // _allowScatter goes last because it has a lower alignment requirement
        _allowScatter = TakeElements<bool>();
    }

    internal BurstQuadBuildData(PQ quad, SingleVertexData* buf, int baseIndex)
        : this(quad, buf, sizeof(SingleVertexData), 1, baseIndex) { }

    public readonly void CopyTo(PQS.VertexBuildData vbdata, int index)
    {
        if ((uint)index >= (uint)_vertexCount)
            throw new IndexOutOfRangeException();

        vbdata.vertIndex = vertIndex[index];
        vbdata.gnomonicPlane = gnomonicPlane;

        vbdata.globalV = _globalV[index];
        vbdata.directionFromCenter = _directionFromCenter[index];
        vbdata.directionD = _directionD[index];
        vbdata.directionXZ = _directionXZ[index];
        vbdata.vertHeight = _vertHeight[index];
        vbdata.vertColor = _vertColor[index];
        vbdata.u = _u[index];
        vbdata.v = _v[index];
        vbdata.u2 = _u2[index];
        vbdata.v2 = _v2[index];
        vbdata.u3 = _u3[index];
        vbdata.v3 = _v3[index];
        vbdata.u4 = _u4[index];
        vbdata.v4 = _v4[index];
        vbdata.gnomonicU = _gnomonicU[index];
        vbdata.gnomonicV = _gnomonicV[index];
        vbdata.allowScatter = _allowScatter[index];
        vbdata.longitude = _longitude[index];
        vbdata.latitude = _latitude[index];

        for (int i = 0; i < 6; ++i)
            vbdata.gnomonicUVs[i] = _gnomonicUVs[index][i];
    }

    public readonly void CopyFrom(PQS.VertexBuildData vbdata, int index)
    {
        if ((uint)index >= (uint)_vertexCount)
            throw new IndexOutOfRangeException();

        _globalV[index] = vbdata.globalV;
        _directionFromCenter[index] = vbdata.directionFromCenter;
        _directionD[index] = vbdata.directionD;
        _directionXZ[index] = vbdata.directionXZ;
        _vertHeight[index] = vbdata.vertHeight;
        _vertColor[index] = vbdata.vertColor;
        _u[index] = vbdata.u;
        _v[index] = vbdata.v;
        _u2[index] = vbdata.u2;
        _v2[index] = vbdata.v2;
        _u3[index] = vbdata.u3;
        _v3[index] = vbdata.v3;
        _u4[index] = vbdata.u4;
        _v4[index] = vbdata.v4;
        _gnomonicU[index] = vbdata.gnomonicU;
        _gnomonicV[index] = vbdata.gnomonicV;
        _allowScatter[index] = vbdata.allowScatter;
        _longitude[index] = vbdata.longitude;
        _latitude[index] = vbdata.latitude;

        for (int i = 0; i < 6; ++i)
            _gnomonicUVs[index][i] = vbdata.gnomonicUVs[i];
    }

    public static int GetRequiredBufferSize(int vertexCount)
    {
        if (vertexCount < 0)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if ((ulong)vertexCount * (ulong)QuadBuildDataExt.CachedElementSize > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(vertexCount),
                "total buffer size would be larger than int.MaxValue"
            );

        return QuadBuildDataExt.CachedElementSize * vertexCount;
    }

    public readonly struct VertexIndices : IEnumerable<int>
    {
        readonly int baseIndex;
        readonly int length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal VertexIndices(
            [AssumeRange(0, int.MaxValue)] int baseIndex,
            [AssumeRange(0, int.MaxValue)] int length
        )
        {
            this.baseIndex = baseIndex;
            this.length = length;
        }

        public int this[int index]
        {
            get
            {
                if ((uint)index >= length)
                    BurstException.ThrowIndexOutOfRange();

                return baseIndex + index;
            }
        }

        public RangeEnumerator GetEnumerator() => new(baseIndex, baseIndex + length);

        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

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
}

internal static class QuadBuildDataExt
{
    internal static readonly int CachedElementSize = GetCachedElementSize();

    static int GetCachedElementSize()
    {
        var type = typeof(BurstQuadBuildData);
        int size = 0;

        foreach (var prop in type.GetProperties())
        {
            var pty = prop.PropertyType;
            if (!pty.IsGenericType)
                continue;

            if (pty.GetGenericTypeDefinition() != typeof(MemorySpan<>))
                continue;

            size += Marshal.SizeOf(pty.GetGenericArguments()[0]);
        }

        return size;
    }
}

// This needs to match the data layout used by BurstQuadBuildData
[StructLayout(LayoutKind.Sequential)]
internal struct SingleVertexData
{
    public Vector3d globalV;
    public Vector3d directionFromCenter;
    public Vector3d directionD;
    public Vector3d directionXZ;
    public double vertHeight;
    public Color vertColor;
    public double u;
    public double v;
    public double u2;
    public double v2;
    public double u3;
    public double v3;
    public double u4;
    public double v4;
    public double gnomonicU;
    public double gnomonicV;
    public double longitude;
    public double latitude;
    public FixedArray6<PQS.GnomonicUV> gnomonicUVs;
    public bool allowScatter;

    public SingleVertexData() { }

    public SingleVertexData(PQS.VertexBuildData data) { }

    public void CopyFrom(PQS.VertexBuildData data)
    {
        globalV = data.globalV;
        directionFromCenter = data.directionFromCenter;
        directionD = data.directionD;
        directionXZ = data.directionXZ;
        vertHeight = data.vertHeight;
        vertColor = data.vertColor;
        u = data.u;
        v = data.v;
        u2 = data.u2;
        v2 = data.v2;
        u3 = data.u3;
        v3 = data.v3;
        u4 = data.u4;
        v4 = data.v4;
        gnomonicU = data.gnomonicU;
        gnomonicV = data.gnomonicV;
        longitude = data.longitude;
        latitude = data.latitude;
        for (int i = 0; i < 6; ++i)
            gnomonicUVs[i] = data.gnomonicUVs[i];
        allowScatter = data.allowScatter;
    }

    public readonly void CopyTo(PQS.VertexBuildData data)
    {
        data.globalV = globalV;
        data.directionFromCenter = directionFromCenter;
        data.directionD = directionD;
        data.directionXZ = directionXZ;
        data.vertHeight = vertHeight;
        data.vertColor = vertColor;
        data.u = u;
        data.v = v;
        data.u2 = u2;
        data.v2 = v2;
        data.u3 = u3;
        data.v3 = v3;
        data.u4 = u4;
        data.v4 = v4;
        data.gnomonicU = gnomonicU;
        data.gnomonicV = gnomonicV;
        data.longitude = longitude;
        data.latitude = latitude;
        for (int i = 0; i < 6; ++i)
            data.gnomonicUVs[i] = gnomonicUVs[i];
        data.allowScatter = allowScatter;
    }
}
