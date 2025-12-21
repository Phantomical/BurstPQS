using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightOffset))]
public class VertexHeightOffset(PQSMod_VertexHeightOffset mod)
    : BatchPQSMod<PQSMod_VertexHeightOffset>(mod),
        IBatchPQSModState
{
    public void OnQuadBuilt(QuadBuildData data) { }

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildHeightsJob { data = data.burst, offset = mod.offset };
        return job.Schedule(handle);
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public double offset;

        public void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += offset;
        }
    }
}
