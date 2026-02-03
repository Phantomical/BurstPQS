using System;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS;

internal class MeshData : IDisposable
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
