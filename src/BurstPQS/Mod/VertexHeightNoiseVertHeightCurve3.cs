using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightNoiseVertHeightCurve3
    : BatchPQSModV1<PQSMod_VertexHeightNoiseVertHeightCurve3>
{
    public VertexHeightNoiseVertHeightCurve3(PQSMod_VertexHeightNoiseVertHeightCurve3 mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        var p = new Params
        {
            sphereRadiusMin = mod.sphere.radiusMin,
            inputHeightStart = mod.inputHeightStart,
            inputHeightEnd = mod.inputHeightEnd,
            deformityMax = mod.deformityMax,
            deformityMin = mod.deformityMin,
            hDeltaR = mod.hDeltaR,
        };

        using var bcurveMult = new BurstSimplex(mod.curveMultiplier.fractal);
        using var bdeformity = new BurstSimplex(mod.deformity.fractal);
        using var binputHeightCurve = new BurstAnimationCurve(mod.inputHeightCurve);

        BuildVertex(
            in data.burstData,
            new(mod.ridgedAdd.fractal),
            new(mod.ridgedSub.fractal),
            in bcurveMult,
            in bdeformity,
            in binputHeightCurve,
            in p
        );
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

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertex(
        [NoAlias] in BurstQuadBuildDataV1 data,
        [NoAlias] in BurstRidgedMultifractal ridgedAdd,
        [NoAlias] in BurstRidgedMultifractal ridgedSub,
        [NoAlias] in BurstSimplex curveMultiplier,
        [NoAlias] in BurstSimplex deformity,
        [NoAlias] in BurstAnimationCurve inputHeightCurve,
        [NoAlias] in Params p
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
                d = MathUtil.Lerp(p.deformityMin, p.deformityMax, deformity.noiseNormalized(dir));
                r = MathUtil.Clamp(r, -1.0, 1.0);
                r = (r + 1.0) * 0.5;

                data.vertHeight[i] += r * d * s;
            }
        }
    }
}
