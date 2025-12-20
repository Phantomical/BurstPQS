using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexRidgedAltitudeCurve))]
public class VertexRidgedAltitudeCurve(PQSMod_VertexRidgedAltitudeCurve mod)
    : BatchPQSMod<PQSMod_VertexRidgedAltitudeCurve>(mod),
        IBatchPQSModState
{
    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data) => this;

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var bsimplex = new BurstSimplex(mod.simplex);
        var bsimplexCurve = new BurstAnimationCurve(mod.simplexCurve);

        var job = new BuildHeightsJob
        {
            data = data.burst,
            simplex = bsimplex,
            ridgedAdd = new(mod.ridgedAdd),
            simplexCurve = bsimplexCurve,
            simplexHeightStart = mod.simplexHeightStart,
            radiusMin = mod.sphere != null ? mod.sphere.radiusMax : mod.ridgedMinimum,
            hDeltaR = mod.hDeltaR,
            ridgedMinimum = mod.ridgedMinimum,
            deformity = mod.deformity,
        };
        handle = job.Schedule(handle);
        bsimplex.Dispose(handle);
        bsimplexCurve.Dispose(handle);

        return handle;
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstSimplex simplex;
        public BurstRidgedMultifractal ridgedAdd;
        public BurstAnimationCurve simplexCurve;
        public double simplexHeightStart;
        public double radiusMin;
        public double hDeltaR;
        public double ridgedMinimum;
        public double deformity;

        public void Execute()
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
}
