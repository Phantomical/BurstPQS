using BurstPQS.Noise;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeight))]
public class VertexSimplexHeight(PQSMod_VertexSimplexHeight mod)
    : InlineBatchPQSMod<PQSMod_VertexSimplexHeight>(mod)
{
    BurstSimplex simplex = new(mod.simplex);

    public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildHeightsJob
        {
            data = data.burst,
            simplex = simplex,
            deformity = mod.deformity,
        };

        return job.Schedule(handle);
    }

    public override void Dispose()
    {
        simplex.Dispose();
    }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstSimplex simplex;
        public double deformity;

        public void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertHeight[i] += simplex.noise(data.directionFromCenter[i]) * deformity;
            }
        }
    }
}
