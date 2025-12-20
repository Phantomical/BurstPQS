using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeightCurve2))]
public class VertexHeightNoiseVertHeightCurve2(PQSMod_VertexHeightNoiseVertHeightCurve2 mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeightCurve2>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var bsimplex = new BurstSimplex(mod.simplex);
        var bcurve = new BurstAnimationCurve(mod.simplexCurve);

        var job = new BuildHeightsJob
        {
            data = data.burst,
            ridgedAdd = new(mod.ridgedAdd),
            ridgedSub = new(mod.ridgedSub),
            simplex = bsimplex,
            simplexCurve = bcurve,
            simplexHeightStart = mod.simplexHeightStart,
            simplexHeightEnd = mod.simplexHeightEnd,
            deformity = mod.deformity,
            hDeltaR = mod.hDeltaR,
        };

        handle = job.Schedule(handle);
        bsimplex.Dispose(handle);
        bcurve.Dispose(handle);

        return handle;
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstRidgedMultifractal ridgedAdd;
        public BurstRidgedMultifractal ridgedSub;
        public BurstSimplex simplex;
        public BurstAnimationCurve simplexCurve;
        public double simplexHeightStart;
        public double simplexHeightEnd;
        public double hDeltaR;
        public float deformity;

        public void Execute()
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
