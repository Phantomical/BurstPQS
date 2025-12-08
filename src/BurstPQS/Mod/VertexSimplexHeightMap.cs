using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexHeightMap : BatchPQSModV1<PQSMod_VertexSimplexHeightMap>
{
    public VertexSimplexHeightMap(PQSMod_VertexSimplexHeightMap mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        using var bsimplex = new BurstSimplex(mod.simplex);
        using var bheightMap = new BurstMapSO(mod.heightMap);

        BuildHeights(
            in data.burstData,
            in bsimplex,
            in bheightMap,
            mod.heightStart,
            mod.heightEnd,
            mod.deformity
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildDataV1 data,
        [NoAlias] in BurstSimplex simplex,
        [NoAlias] in BurstMapSO heightMap,
        double heightStart,
        double heightEnd,
        double deformity
    )
    {
        double hDeltaR = 1.0 / (heightEnd - heightStart);

        for (int i = 0; i < data.VertexCount; ++i)
        {
            double h = heightMap.GetPixelFloat(data.u[i], data.v[i]);
            if (h < heightStart || h > heightEnd)
                continue;

            h = (h - heightStart) * hDeltaR;
            data.vertHeight[i] +=
                (simplex.noise(data.directionFromCenter[i]) + 1.0) * 0.5 * deformity * h;
        }
    }
}
