using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexRidgedAltitudeCurve : BatchPQSModV1<PQSMod_VertexRidgedAltitudeCurve>
{
    public VertexRidgedAltitudeCurve(PQSMod_VertexRidgedAltitudeCurve mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        using var g0 = BurstSimplex.Create(mod.simplex, out var bsimplex);
        using var g1 = BurstAnimationCurve.Create(mod.simplexCurve, out var bsimplexCurve);

        BuildHeights(
            in data.burstData,
            in bsimplex,
            new(mod.ridgedAdd),
            in bsimplexCurve,
            mod.simplexHeightStart,
            mod.sphere != null ? mod.sphere.radiusMax : mod.ridgedMinimum,
            mod.hDeltaR,
            mod.ridgedMinimum,
            mod.deformity
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildDataV1 data,
        [NoAlias] in BurstSimplex simplex,
        [NoAlias] in RidgedMultifractal ridgedAdd,
        [NoAlias] in BurstAnimationCurve simplexCurve,
        double simplexHeightStart,
        double radiusMin,
        double hDeltaR,
        double ridgedMinimum,
        double deformity
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            double h = data.vertHeight[i] - radiusMin;
            double t = MathUtil.Clamp01((h - simplexHeightStart) * hDeltaR);
            double s = simplex.noiseNormalized(data.directionFromCenter[i]);
            if (s == 0.0)
                continue;

            double r = MathUtil.Clamp(
                Math.Max(ridgedMinimum, ridgedAdd.GetValue(data.directionFromCenter[i])),
                -1.0,
                1.0
            );

            data.vertHeight[i] += r * deformity * simplexCurve.Evaluate((float)t);
        }
    }
}
