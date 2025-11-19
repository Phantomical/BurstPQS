using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorMapBlend : BatchPQSModV1<PQSMod_VertexColorMapBlend>
{
    public VertexColorMapBlend(PQSMod_VertexColorMapBlend mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(mod.vertexColorMap, out var mapSO);
        BuildVertices(in data.burstData, in mapSO, mod.blend);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstMapSO vertexColorMap,
        float blend
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertColor[i] = Color.Lerp(
                data.vertColor[i],
                vertexColorMap.GetPixelColor(data.u[i], data.v[i]),
                blend
            );
        }
    }
}
