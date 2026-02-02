using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexRidgedAltitudeCurve))]
public class VertexRidgedAltitudeCurve(PQSMod_VertexRidgedAltitudeCurve mod)
    : BatchPQSMod<PQSMod_VertexRidgedAltitudeCurve>(mod)
{
    BurstAnimationCurve simplexCurve = new(mod.simplexCurve);
    BurstSimplex simplex = new(mod.simplex);

    public override void Dispose()
    {
        simplexCurve.Dispose();
        simplex.Dispose();
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildHeightsJob
            {
                simplex = simplex,
                ridgedAdd = new(mod.ridgedAdd),
                simplexCurve = simplexCurve,
                simplexHeightStart = mod.simplexHeightStart,
                radiusMin = mod.sphere != null ? mod.sphere.radiusMin : mod.ridgedMinimum,
                hDeltaR = mod.hDeltaR,
                ridgedMinimum = mod.ridgedMinimum,
                deformity = mod.deformity,
            }
        );
    }

    [BurstCompile]
    struct BuildHeightsJob : IBatchPQSHeightJob
    {
        public BurstSimplex simplex;
        public BurstRidgedMultifractal ridgedAdd;
        public BurstAnimationCurve simplexCurve;
        public double simplexHeightStart;
        public double radiusMin;
        public double hDeltaR;
        public double ridgedMinimum;
        public double deformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = data.vertHeight[i] - radiusMin;
                double t = MathUtil.Clamp01((h - simplexHeightStart) * hDeltaR);
                double s = simplex.noiseNormalized(data.directionFromCenter[i]);
                if (s == 0.0)
                    continue;

                double r = MathUtil.Clamp(
                    Math.Max(ridgedMinimum, ridgedAdd.GetValue(data.directionFromCenter[i])) * Math.Max(s, 0.0),
                    -1.0,
                    1.0
                );

                data.vertHeight[i] += r * deformity * simplexCurve.Evaluate((float)t);
            }
        }
    }
}
