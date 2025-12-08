using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexHeightFlatten : BatchPQSModV1<PQSMod_VertexSimplexHeightFlatten>
{
    public VertexSimplexHeightFlatten(PQSMod_VertexSimplexHeightFlatten mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        using var bsimplex = new BurstSimplex(mod.simplex);

        BuildHeights(in data.burstData, in bsimplex, mod.deformity, mod.cutoff);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildDataV1 data,
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
