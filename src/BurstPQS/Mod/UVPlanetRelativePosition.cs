using System;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_UVPlanetRelativePosition))]
public class UVPlanetRelativePosition(PQSMod_UVPlanetRelativePosition mod)
    : BatchPQSMod<PQSMod_UVPlanetRelativePosition>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);
        jobSet.Add(new BuildJob());
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSMeshJob
    {
        public readonly void BuildMesh(in BuildMeshData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var v = data.vertsD[i];
                var n = data.normals[i];

                data.uvs[i].x = (float)v.x;
                data.uvs[i].y = (float)v.y;
                data.uv2s[i].x = (float)v.z;
                data.uv2s[i].y = (float)(1.0 - Vector3d.Dot(v.Normalized(), n));
            }
        }
    }

    internal static unsafe void UpdateQuadNormals(PQ quad)
    {
        UpdateQuadNormalsFunc ??= BurstUtil
            .MaybeCompileFunctionPointer<UpdateQuadNormalsDelegate>(UpdateQuadNormalsBurst)
            .Invoke;

        if (quad.vertNormals.Length != quad.verts.Length)
            throw new IndexOutOfRangeException(
                "quad normals array did not have the correct length"
            );
        if (PQS.cacheUV2s.Length != quad.verts.Length)
            throw new IndexOutOfRangeException("PQS.cacheUV2s did not have the correct length");

        fixed (Vector3* verts = quad.verts)
        fixed (Vector3* vertNormals = quad.vertNormals)
        fixed (Vector2* uv2s = PQS.cacheUV2s)
        {
            UpdateQuadNormalsFunc(
                quad.positionPlanet,
                new(verts, quad.verts.Length),
                new(vertNormals, quad.vertNormals.Length),
                new(uv2s, PQS.cacheUV2s.Length)
            );
        }
    }

    delegate void UpdateQuadNormalsDelegate(
        in Vector3d positionPlanet,
        in MemorySpan<Vector3> verts,
        in MemorySpan<Vector3> vertNormals,
        in MemorySpan<Vector2> uv2s
    );

    static UpdateQuadNormalsDelegate UpdateQuadNormalsFunc;

    [BurstCompile]
    static void UpdateQuadNormalsBurst(
        in Vector3d positionPlanet,
        in MemorySpan<Vector3> verts,
        in MemorySpan<Vector3> vertNormals,
        in MemorySpan<Vector2> uv2s
    )
    {
        for (int i = 0; i < verts.Length; ++i)
        {
            var v = verts[i] + positionPlanet;
            uv2s[i].x = (float)v.z;
            uv2s[i].y = (float)(1.0 - Vector3d.Dot(v.Normalized(), vertNormals[i]));
        }
    }
}
