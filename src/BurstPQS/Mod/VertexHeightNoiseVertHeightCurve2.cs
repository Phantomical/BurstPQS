using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeightCurve2))]
public class VertexHeightNoiseVertHeightCurve2(PQSMod_VertexHeightNoiseVertHeightCurve2 mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeightCurve2>(mod)
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
                ridgedAdd = new(mod.ridgedAdd),
                ridgedSub = new(mod.ridgedSub),
                simplex = simplex,
                simplexCurve = simplexCurve,
                simplexHeightStart = mod.simplexHeightStart,
                simplexHeightEnd = mod.simplexHeightEnd,
                deformity = mod.deformity,
                hDeltaR = mod.hDeltaR,
            }
        );
    }

    [BurstCompile]
    struct BuildHeightsJob : IBatchPQSHeightJob
    {
        public BurstRidgedMultifractal ridgedAdd;
        public BurstRidgedMultifractal ridgedSub;
        public BurstSimplex simplex;
        public BurstAnimationCurve simplexCurve;
        public double simplexHeightStart;
        public double simplexHeightEnd;
        public double hDeltaR;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            double h;
            double s;
            double r;
            float t;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                h = data.vertHeight[i] - data.sphere.radiusMin;
                if (h <= simplexHeightStart)
                    t = 0f;
                else if (h >= simplexHeightEnd)
                    t = 1f;
                else
                    t = (float)((h - simplexHeightStart) * hDeltaR);

                s = simplex.noiseNormalized(data.directionFromCenter[i]) * simplexCurve.Evaluate(t);
                if (s != 0.0)
                {
                    r = MathUtil.Clamp(
                        ridgedAdd.GetValue(data.directionFromCenter[i])
                            - ridgedSub.GetValue(data.directionFromCenter[i]),
                        -1.0,
                        1.0
                    );

                    data.vertHeight[i] += (r + 1.0) * 0.5 * deformity * s;
                }
            }
        }
    }
}
