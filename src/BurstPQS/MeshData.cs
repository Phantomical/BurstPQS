using System;
using System.Collections.Generic;
using KSP.UI.Screens.DebugToolbar.Screens.Cheats;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

#pragma warning disable IDE1006 // Naming Styles

namespace BurstPQS;

internal struct MeshDataStruct : IDisposable
{
    public NativeArray<Vector3> verts;
    public NativeArray<Vector3d> vertsD;
    public NativeArray<Vector3> normals;
    public NativeArray<Vector4> tangents;
    public NativeArray<Color> colors;
    public NativeArray<Vector2> uv0;
    public NativeArray<Vector2> uv1;
    public NativeArray<Vector2> uv2;
    public NativeArray<Vector2> uv3;

    public void Dispose()
    {
        verts.Dispose();
        vertsD.Dispose();
        normals.Dispose();
        tangents.Dispose();
        colors.Dispose();
        uv0.Dispose();
        uv1.Dispose();
        uv2.Dispose();
        uv3.Dispose();
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

    public NativeArray<Vector3> verts => data.verts;
    public NativeArray<Vector3d> vertsD => data.vertsD;
    public NativeArray<Vector3> normals => data.normals;
    public NativeArray<Vector4> tangents => data.tangents;
    public NativeArray<Color> colors => data.colors;
    public NativeArray<Vector2> uv0 => data.uv0;
    public NativeArray<Vector2> uv1 => data.uv1;
    public NativeArray<Vector2> uv2 => data.uv2;
    public NativeArray<Vector2> uv3 => data.uv3;

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
