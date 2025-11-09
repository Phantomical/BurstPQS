using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexHeightAbsolute : PQSMod_VertexSimplexHeightAbsolute, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        using var g0 = BurstSimplex.Create(simplex, out var bsimplex);

        BuildHeights(in data.burstData, in bsimplex, deformity);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildData data,
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
