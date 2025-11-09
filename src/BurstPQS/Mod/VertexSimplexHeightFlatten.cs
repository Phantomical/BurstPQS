using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexHeightFlatten : BatchPQSMod<PQSMod_VertexSimplexHeightFlatten>
{
    public VertexSimplexHeightFlatten(PQSMod_VertexSimplexHeightFlatten mod)
        : base(mod) { }

    public override void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        using var g0 = BurstSimplex.Create(mod.simplex, out var bsimplex);

        BuildHeights(in data.burstData, in bsimplex, mod.deformity, mod.cutoff);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstSimplex simplex,
        double deformity,
        double cutoff
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            double v = simplex.noiseNormalized(data.directionFromCenter[i]);
            if (v > cutoff)
                data.vertHeight[i] += deformity * ((v - cutoff) / cutoff);
        }
    }
}
