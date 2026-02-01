using System;
using BurstPQS.Noise;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeightFlatten))]
public class VertexSimplexHeightFlatten(PQSMod_VertexSimplexHeightFlatten mod) : BatchPQSMod<PQSMod_VertexSimplexHeightFlatten>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob
        {
            simplex = new BurstSimplex(mod.simplex),
            deformity = mod.deformity,
            cutoff = mod.cutoff
        });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstSimplex simplex;
        public double deformity;
        public double cutoff;

        public void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double v = simplex.noiseNormalized(data.directionFromCenter[i]);
                if (v > cutoff)
                    data.vertHeight[i] += deformity * ((v - cutoff) / cutoff);
            }
        }

        public void Dispose()
        {
            simplex.Dispose();
        }
    }
}
