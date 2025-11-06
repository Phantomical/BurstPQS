using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightNoiseVertHeightCurve2
    : PQSMod_VertexHeightNoiseVertHeightCurve2,
        IBatchPQSMod
{
    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        var p = new Params
        {
            sphereRadiusMin = sphere.radiusMin,
            simplexHeightStart = simplexHeightStart,
            simplexHeightEnd = simplexHeightEnd,
            deformity = deformity,
            hDeltaR = hDeltaR,
        };

        using var guard1 = BurstSimplex.Create(simplex, out var bsimplex);
        using var guard2 = BurstAnimationCurve.Create(simplexCurve, out var bcurve);

        BuildVertex(
            in data.burstData,
            new(ridgedAdd),
            new(ridgedSub),
            in bsimplex,
            in bcurve,
            in p
        );
    }

    public void OnQuadBuildVertex(in QuadBuildData data) { }

    struct Params
    {
        public double sphereRadiusMin;
        public double simplexHeightStart;
        public double simplexHeightEnd;
        public double hDeltaR;
        public float deformity;
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertex(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in RidgedMultifractal ridgedAdd,
        [NoAlias] in RidgedMultifractal ridgedSub,
        [NoAlias] in BurstSimplex simplex,
        [NoAlias] in BurstAnimationCurve simplexCurve,
        [NoAlias] in Params p
    )
    {
        double h;
        double s;
        double r;
        float t;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            h = data.vertHeight[i] - p.sphereRadiusMin;
            if (h <= p.simplexHeightStart)
                t = 0f;
            else if (h >= p.simplexHeightEnd)
                t = 1f;
            else
                t = (float)((h - p.simplexHeightStart) * p.hDeltaR);

            s = simplex.noiseNormalized(data.directionFromCenter[i]) * simplexCurve.Evaluate(t);
            if (s != 0.0)
            {
                r = MathUtil.Clamp(
                    ridgedAdd.GetValue(data.directionFromCenter[i])
                        - ridgedSub.GetValue(data.directionFromCenter[i]),
                    -1.0,
                    1.0
                );

                data.vertHeight[i] += (r + 1.0) * 0.5 * p.deformity * s;
            }
        }
    }
}
