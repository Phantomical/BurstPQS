using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

#pragma warning disable IDE1006 // Naming Styles

namespace BurstPQS;

[StructLayout(LayoutKind.Sequential)]
internal struct InterleavedVertex
{
    public Vector3 position; // 12 bytes
    public Color color; // 16 bytes
    public Vector2 uv0; //  8 bytes
    public Vector2 uv1; //  8 bytes
    public Vector2 uv2; //  8 bytes
    public Vector2 uv3; //  8 bytes
} // 60 bytes total

internal struct MeshDataStruct : IDisposable
{
    // Mesh streams
    public NativeArray<InterleavedVertex> interleaved; // stream 0
    public NativeArray<Vector3> normals; // stream 1
    public NativeArray<Vector4> tangents; // stream 2 (conditional)

    // PQS cache arrays (deinterleaved on worker thread)
    public NativeArray<Vector3d> vertsD;
    public NativeArray<Color> cacheColors;
    public NativeArray<Vector2> cacheUVs;
    public NativeArray<Vector2> cacheUV2s;
    public NativeArray<Vector2> cacheUV3s;
    public NativeArray<Vector2> cacheUV4s;

    public void Dispose()
    {
        interleaved.Dispose();
        normals.Dispose();
        tangents.Dispose();
        vertsD.Dispose();
        cacheColors.Dispose();
        cacheUVs.Dispose();
        cacheUV2s.Dispose();
        cacheUV3s.Dispose();
        cacheUV4s.Dispose();
    }

    struct DisposeJob(MeshDataStruct data) : IJob
    {
        MeshDataStruct data = data;

        public void Execute() => data.Dispose();
    }

    public void Dispose(JobHandle dependsOn)
    {
        new DisposeJob(this).Schedule(dependsOn);
        this = default;
    }
}

internal class MeshData : IDisposable
{
    public MeshDataStruct data;

    public NativeArray<InterleavedVertex> interleaved => data.interleaved;
    public NativeArray<Vector3> normals => data.normals;
    public NativeArray<Vector4> tangents => data.tangents;

    public NativeArray<Vector3d> vertsD => data.vertsD;
    public NativeArray<Color> cacheColors => data.cacheColors;
    public NativeArray<Vector2> cacheUVs => data.cacheUVs;
    public NativeArray<Vector2> cacheUV2s => data.cacheUV2s;
    public NativeArray<Vector2> cacheUV3s => data.cacheUV3s;
    public NativeArray<Vector2> cacheUV4s => data.cacheUV4s;

    private const int MaxPoolItems = 256;
    private static readonly Stack<MeshData> Pool = [];

    public static MeshData Acquire()
    {
        if (Pool.TryPop(out var data))
            return data;
        return new();
    }

    public void Dispose()
    {
        data.Dispose(default);

        if (Pool.Count < MaxPoolItems)
            Pool.Push(this);
    }
}
