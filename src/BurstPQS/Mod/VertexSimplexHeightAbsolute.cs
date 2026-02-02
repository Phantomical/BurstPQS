using System;
using BurstPQS.Noise;
using Unity.Burst;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeightAbsolute))]
public class VertexSimplexHeightAbsolute(PQSMod_VertexSimplexHeightAbsolute mod)
    : BatchPQSMod<PQSMod_VertexSimplexHeightAbsolute>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob { simplex = new BurstSimplex(mod.simplex), deformity = mod.deformity }
        );
    }

    // [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstSimplex simplex;
        public double deformity;

        public void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertHeight[i] +=
                    (simplex.noise(data.directionFromCenter[i]) + 1.0) * 0.5 * deformity;
            }
        }

        public void Dispose()
        {
            simplex.Dispose();
        }
    }
}
