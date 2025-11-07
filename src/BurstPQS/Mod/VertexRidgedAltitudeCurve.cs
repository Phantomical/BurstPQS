using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexRidgedAltitudeCurve : PQSMod_VertexRidgedAltitudeCurve, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        using var g0 = BurstSimplex.Create(simplex, out var bsimplex);
        using var g1 = BurstAnimationCurve.Create(simplexCurve, out var bsimplexCurve);

        BuildHeights(
            in data.burstData,
            in bsimplex,
            new(ridgedAdd),
            in bsimplexCurve,
            simplexHeightStart,
            sphere != null ? sphere.radiusMax : ridgedMinimum,
            hDeltaR,
            ridgedMinimum,
            deformity
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildData data,
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
