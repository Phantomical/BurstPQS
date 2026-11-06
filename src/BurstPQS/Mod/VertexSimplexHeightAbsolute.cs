using System;
using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
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

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstSimplex simplex;
        public double deformity;

        public void BuildHeights(in BuildHeightsData data)
        {
            using var noise = simplex.GetValuesNormalized(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += noise[i] * deformity;
        }

        public void Dispose()
        {
            simplex.Dispose();
        }
    }
}
