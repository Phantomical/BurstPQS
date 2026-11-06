using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeight))]
public class VertexSimplexHeight(PQSMod_VertexSimplexHeight mod)
    : BatchPQSMod<PQSMod_VertexSimplexHeight>(mod)
{
    BurstSimplex simplex = new(mod.simplex);

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { simplex = simplex, deformity = mod.deformity });
    }

    public override void Dispose()
    {
        simplex.Dispose();
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public BurstSimplex simplex;
        public double deformity;

        public void BuildHeights(in BuildHeightsData data)
        {
            using var noise = simplex.GetValues(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += noise[i] * deformity;
        }
    }
}
