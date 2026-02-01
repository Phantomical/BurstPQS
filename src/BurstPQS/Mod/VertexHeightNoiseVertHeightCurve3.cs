using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeightCurve3))]
public class VertexHeightNoiseVertHeightCurve3(PQSMod_VertexHeightNoiseVertHeightCurve3 mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeightCurve3>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob
        {
            ridgedAdd = new(mod.ridgedAdd.fractal),
            ridgedSub = new(mod.ridgedSub.fractal),
            curveMultiplier = new BurstSimplex(mod.curveMultiplier.fractal),
            deformity = new BurstSimplex(mod.deformity.fractal),
            inputHeightCurve = new BurstAnimationCurve(mod.inputHeightCurve),
            p = new Params
            {
                sphereRadiusMin = mod.sphere.radiusMin,
                inputHeightStart = mod.inputHeightStart,
                inputHeightEnd = mod.inputHeightEnd,
                deformityMax = mod.deformityMax,
                deformityMin = mod.deformityMin,
                hDeltaR = mod.hDeltaR,
            },
        });
    }

    struct Params
    {
        public double sphereRadiusMin;
        public double inputHeightStart;
        public double inputHeightEnd;
        public double deformityMin;
        public double deformityMax;
        public double hDeltaR;
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstRidgedMultifractal ridgedAdd;
        public BurstRidgedMultifractal ridgedSub;
        public BurstSimplex curveMultiplier;
        public BurstSimplex deformity;
        public BurstAnimationCurve inputHeightCurve;
        public Params p;

        public void BuildHeights(in BuildHeightsData data)
        {
            double r;
            double d;
            float t;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = data.vertHeight[i] - p.sphereRadiusMin;
                if (h <= p.inputHeightStart)
                    t = 0f;
                else if (h >= p.inputHeightEnd)
                    t = 1f;
                else
                    t = (float)((h - p.inputHeightStart) * p.hDeltaR);

                var dir = data.directionFromCenter[i];
                double s = curveMultiplier.noiseNormalized(dir) * inputHeightCurve.Evaluate(t);
                if (s != 0.0)
                {
                    r = ridgedAdd.GetValue(dir) - ridgedSub.GetValue(dir);
                    d = MathUtil.Lerp(p.deformityMin, p.deformityMax, deformity.noiseNormalized(dir));
                    r = MathUtil.Clamp(r, -1.0, 1.0);
                    r = (r + 1.0) * 0.5;

                    data.vertHeight[i] += r * d * s;
                }
            }
        }

        public void Dispose()
        {
            curveMultiplier.Dispose();
            deformity.Dispose();
            inputHeightCurve.Dispose();
        }
    }
}
