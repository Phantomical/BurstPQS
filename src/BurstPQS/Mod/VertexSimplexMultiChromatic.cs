using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexMultiChromatic : BatchPQSModV1<PQSMod_VertexSimplexMultiChromatic>
{
    public VertexSimplexMultiChromatic(PQSMod_VertexSimplexMultiChromatic mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildDataV1 data)
    {
        using var brSimplex = new BurstSimplex(mod.redSimplex);
        using var bbSimplex = new BurstSimplex(mod.blueSimplex);
        using var bgSimplex = new BurstSimplex(mod.greenSimplex);
        using var baSimplex = new BurstSimplex(mod.alphaSimplex);

        BuildVertices(
            in data.burstData,
            in brSimplex,
            in bbSimplex,
            in bgSimplex,
            in baSimplex,
            mod.blend
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildDataV1 data,
        [NoAlias] in BurstSimplex rSimplex,
        [NoAlias] in BurstSimplex gSimplex,
        [NoAlias] in BurstSimplex bSimplex,
        [NoAlias] in BurstSimplex aSimplex,
        float blend
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var dir = data.directionFromCenter[i];
            var c = new Color(
                (float)rSimplex.noiseNormalized(dir),
                (float)bSimplex.noiseNormalized(dir),
                (float)gSimplex.noiseNormalized(dir),
                (float)aSimplex.noiseNormalized(dir)
            );

            data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
        }
    }
}
