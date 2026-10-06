using System;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86;
using UnityEngine;
using UnityEngine.Rendering;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_UVPlanetRelativePosition))]
public class UVPlanetRelativePosition(PQSMod_UVPlanetRelativePosition mod)
    : BatchPQSMod<PQSMod_UVPlanetRelativePosition>(mod)
{
    public override void OnSetup()
    {
        var pqs = mod.sphere;

        pqs.modRequirements |= PQS.ModiferRequirements.MeshUV2;
        pqs.modRequirements |= PQS.ModiferRequirements.UVQuadCoords;
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);
        jobSet.Add(new BuildJob());
    }

    public override void OnQuadBuilt(PQ quad)
    {
        // don't call into the default OnQuadBuilt since it is already handled
        // by BuildJob.BuildMesh.
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
                double mag = Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
                var vn = mag > 0.0 ? v / mag : Vector3d.zero;
                data.uv2s[i].y = (float)(1.0 - Vector3d.Dot(vn, n));
            }
        }
    }

    internal static unsafe void UpdateQuadNormals(PQ quad)
    {
        UpdateQuadNormalsFunc ??= BurstUtil.MaybeCompileDelegate<UpdateQuadNormalsDelegate>(
            UpdateQuadNormalsBurst
        );

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

        if (!BatchPQS.TrySetQuadStream(quad.mesh, PQS.cacheUV2s, VertexAttribute.TexCoord1))
            quad.mesh.uv2 = PQS.cacheUV2s;
    }

    delegate void UpdateQuadNormalsDelegate(
        in Vector3d positionPlanet,
        in MemorySpan<Vector3> verts,
        in MemorySpan<Vector3> vertNormals,
        in MemorySpan<Vector2> uv2s
    );

    static UpdateQuadNormalsDelegate UpdateQuadNormalsFunc;

    [BurstCompile(FloatMode = FloatMode.Fast)]
    static unsafe void UpdateQuadNormalsBurst(
        [NoAlias] in Vector3d positionPlanet,
        [NoAlias] in MemorySpan<Vector3> verts,
        [NoAlias] in MemorySpan<Vector3> vertNormals,
        [NoAlias] in MemorySpan<Vector2> uv2s
    )
    {
        if (verts.Length != vertNormals.Length)
            return;
        if (verts.Length != uv2s.Length)
            return;

        UpdateQuadNormalsImpl(
            BurstUtil.ConvertVector(positionPlanet),
            (float3*)verts.GetDataPtr(),
            (float3*)vertNormals.GetDataPtr(),
            (float2*)uv2s.GetDataPtr(),
            verts.Length
        );
    }

    static unsafe void UpdateQuadNormalsImpl(
        double3 positionPlanet,
        [NoAlias] float3* verts,
        [NoAlias] float3* vertNormals,
        [NoAlias] float2* uv2s,
        int count
    )
    {
        int i = 0;
        for (; i + 4 <= count; i += 4)
        {
            // Transpose 4 vertices into SoA so the math runs 4-wide.
            MathUtil.LoadTransposed(verts + i, out var vx, out var vy, out var vz);
            MathUtil.LoadTransposed(vertNormals + i, out var nx, out var ny, out var nz);

            // The add needs doubles, but the result only depends on the
            // direction of v, so the rest only needs relative precision.
            float4 x = (float4)((double4)vx + positionPlanet.x);
            float4 y = (float4)((double4)vy + positionPlanet.y);
            float4 z = (float4)((double4)vz + positionPlanet.z);

            float4 dot = x * nx + y * ny + z * nz;
            float4 mag2 = x * x + y * y + z * z;
            float4 u = z;
            float4 w = 1f - dot * MathUtil.RsqrtApprox(mag2);

            var out4 = (float4*)(uv2s + i);
            out4[0] = new float4(u.x, w.x, u.y, w.y);
            out4[1] = new float4(u.z, w.z, u.w, w.w);
        }

        for (; i < count; ++i)
        {
            double3 v = (double3)verts[i] + positionPlanet;
            double3 n = vertNormals[i];
            uv2s[i] = new float2(
                (float)v.z,
                (float)(1.0 - math.dot(v, n) / math.sqrt(math.dot(v, v)))
            );
        }
    }
}
