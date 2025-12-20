using BurstPQS.Noise;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeight))]
public class VertexSimplexHeight(PQSMod_VertexSimplexHeight mod)
    : BatchPQSMod<PQSMod_VertexSimplexHeight>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildHeightsJob
        {
            data = data.burst,
            simplex = new(mod.simplex),
            deformity = mod.deformity,
        };

        handle = job.Schedule(handle);
        job.simplex.Dispose(handle);

        return handle;
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

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
