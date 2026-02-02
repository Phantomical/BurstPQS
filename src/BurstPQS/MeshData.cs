using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BurstPQS;

internal class MeshData : IDisposable
{
    public NativeArray<Vector3> positions;
    public NativeArray<Vector3> normals;
    public NativeArray<Vector4> tangents;
    public NativeArray<Color> colors;
    public NativeArray<Vector2> uv0;
    public NativeArray<Vector2> uv1;
    public NativeArray<Vector2> uv2;
    public NativeArray<Vector2> uv3;

    public void Dispose()
    {
        if (positions.IsCreated) positions.Dispose();
        if (normals.IsCreated) normals.Dispose();
        if (tangents.IsCreated) tangents.Dispose();
        if (colors.IsCreated) colors.Dispose();
        if (uv0.IsCreated) uv0.Dispose();
        if (uv1.IsCreated) uv1.Dispose();
        if (uv2.IsCreated) uv2.Dispose();
        if (uv3.IsCreated) uv3.Dispose();
    }
}
