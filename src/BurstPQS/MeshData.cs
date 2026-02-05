using System;
using Unity.Collections;
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

    public void Dispose()
    {
        data.Dispose();
    }
}
