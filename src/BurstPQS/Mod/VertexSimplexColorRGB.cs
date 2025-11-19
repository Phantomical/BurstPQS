using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexColorRGB : BatchPQSModV1<PQSMod_VertexSimplexColorRGB>
{
    public VertexSimplexColorRGB(PQSMod_VertexSimplexColorRGB mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildData data)
    {
        using var g0 = BurstSimplex.Create(mod.simplex, out var bsimplex);

        BuildVertices(
            in data.burstData,
            in bsimplex,
            mod.rBlend,
            mod.gBlend,
            mod.bBlend,
            mod.blend
        );
    }

    public override void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        using var g0 = BurstSimplex.Create(mod.simplex, out var bsimplex);

        BuildVertices(
            in data.burstData,
            in bsimplex,
            mod.rBlend,
            mod.gBlend,
            mod.bBlend,
            mod.blend
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    public void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstSimplex simplex,
        float rBlend,
        float gBlend,
        float bBlend,
        float blend
    )
    {
        float3 cblend = new(rBlend, gBlend, bBlend);
        for (int i = 0; i < data.VertexCount; ++i)
        {
            float n = (float)simplex.noise(data.directionFromCenter[i]);
            float4 c = new(n * cblend, Color.white.a);

            data.vertColor[i] = Color.Lerp(data.vertColor[i], BurstUtil.ConvertColor(c), blend);
        }
    }
}
