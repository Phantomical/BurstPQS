using BurstPQS.Noise;
using Unity.Burst;

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

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertHeight[i] += simplex.noise(data.directionFromCenter[i]) * deformity;
            }
        }
    }
}
