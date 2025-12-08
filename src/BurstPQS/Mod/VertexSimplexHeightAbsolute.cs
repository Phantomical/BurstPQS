using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexHeightAbsolute : BatchPQSModV1<PQSMod_VertexSimplexHeightAbsolute>
{
    public VertexSimplexHeightAbsolute(PQSMod_VertexSimplexHeightAbsolute mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        using var bsimplex = new BurstSimplex(mod.simplex);

        BuildHeights(in data.burstData, in bsimplex, mod.deformity);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildDataV1 data,
        [NoAlias] in BurstSimplex simplex,
        double deformity
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertHeight[i] +=
                (simplex.noise(data.directionFromCenter[i]) + 1.0) * 0.5 * deformity;
        }
    }
}
