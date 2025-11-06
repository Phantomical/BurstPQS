using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile(FloatMode = FloatMode.Fast)]
public class VertexHeightNoiseVertHeightCurve3
    : PQSMod_VertexHeightNoiseVertHeightCurve3,
        IBatchPQSMod
{
    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        var p = new Params
        {
            sphereRadiusMin = sphere.radiusMin,
            inputHeightStart = inputHeightStart,
            inputHeightEnd = inputHeightEnd,
            deformityMax = deformityMax,
            deformityMin = deformityMin,
            hDeltaR = hDeltaR,
        };

        using var guard1 = BurstSimplex.Create(curveMultiplier.fractal, out var bcurveMult);
        using var guard2 = BurstSimplex.Create(deformity.fractal, out var bdeformity);
        using var guard3 = BurstAnimationCurve.Create(inputHeightCurve, out var binputHeightCurve);

        BuildVertex(
            in data.burstData,
            new(ridgedAdd.fractal),
            new(ridgedSub.fractal),
            in bcurveMult,
            in bdeformity,
            in binputHeightCurve,
            in p
        );
    }

    public void OnQuadBuildVertex(in QuadBuildData data) { }

    struct Params
    {
        public double sphereRadiusMin;
        public double inputHeightStart;
        public double inputHeightEnd;
        public double deformityMin;
        public double deformityMax;
        public double hDeltaR;
    }

    static void BuildVertex(
        in BurstQuadBuildData data,
        in RidgedMultifractal ridgedAdd,
        in RidgedMultifractal ridgedSub,
        in BurstSimplex curveMultiplier,
        in BurstSimplex deformity,
        in BurstAnimationCurve inputHeightCurve,
        in Params p
    )
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
                d = UtilMath.LerpUnclamped(
                    p.deformityMin,
                    p.deformityMax,
                    deformity.noiseNormalized(dir)
                );
                r = UtilMath.Clamp(r, -1.0, 1.0);
                r = (r + 1.0) * 0.5;

                data.vertHeight[i] += r * d * s;
            }
        }
    }
}
