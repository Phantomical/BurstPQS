using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexHeight : BatchPQSModV1<PQSMod_VertexSimplexHeight>
{
    public VertexSimplexHeight(PQSMod_VertexSimplexHeight mod)
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
            data.vertHeight[i] += simplex.noise(data.directionFromCenter[i]) * deformity;
        }
    }
}
