using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeightCurve3))]
public class VertexHeightNoiseVertHeightCurve3(PQSMod_VertexHeightNoiseVertHeightCurve3 mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeightCurve3>(mod)
{
    BurstRidgedMultifractal ridgedAdd;
    BurstRidgedMultifractal ridgedSub;
    BurstSimplex curveMultiplier;
    BurstSimplex deformity;
    BurstAnimationCurve inputHeightCurve;

    public override void OnSetup()
    {
        base.OnSetup();

        ridgedAdd = new(mod.ridgedAdd.fractal);
        ridgedSub = new(mod.ridgedSub.fractal);
        curveMultiplier = new(mod.curveMultiplier.fractal);
        deformity = new(mod.deformity.fractal);
        inputHeightCurve = new(mod.inputHeightCurve);
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                ridgedAdd = ridgedAdd,
                ridgedSub = ridgedSub,
                curveMultiplier = curveMultiplier,
                deformity = deformity,
                inputHeightCurve = inputHeightCurve,
                p = new Params
                {
                    sphereRadiusMin = mod.sphere.radiusMin,
                    inputHeightStart = mod.inputHeightStart,
                    inputHeightEnd = mod.inputHeightEnd,
                    deformityMax = mod.deformityMax,
                    deformityMin = mod.deformityMin,
                    hDeltaR = mod.hDeltaR,
                },
            }
        );
    }

    public override void Dispose()
    {
        curveMultiplier.Dispose();
        deformity.Dispose();
        inputHeightCurve.Dispose();
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
    struct BuildJob : IBatchPQSHeightJob
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

            using var multiplier = curveMultiplier.GetValuesNormalized(
                data.directionFromCenter,
                Allocator.Temp
            );
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
                double s = multiplier[i] * inputHeightCurve.Evaluate(t);
                if (s != 0.0)
                {
                    r = ridgedAdd.GetValue(dir) - ridgedSub.GetValue(dir);
                    d = MathUtil.Lerp(
                        p.deformityMax,
                        p.deformityMin,
                        deformity.noiseNormalized(dir)
                    );
                    r = MathUtil.Clamp(r, -1.0, 1.0);
                    r = (r + 1.0) * 0.5;

                    data.vertHeight[i] += r * d * s;
                }
            }
        }
    }
}
