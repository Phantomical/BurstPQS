using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BurstPQS;

internal class MeshData : IDisposable
{
    public VertexAttributeDescriptor[] descriptors;
    public NativeArray<byte> vertexData;
    public NativeArray<Vector3> normalData;
    public NativeArray<Vector4> tangentData;

    public void Dispose()
    {
        vertexData.Dispose();
        normalData.Dispose();
        tangentData.Dispose();
    }
}
