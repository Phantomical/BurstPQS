using System;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_FlattenOcean))]
public class FlattenOcean(PQSMod_FlattenOcean mod)
    : BatchPQSMod<PQSMod_FlattenOcean>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildHeightsJob { data = data.burst, oceanRad = mod.oceanRad };
        return job.Schedule(handle);
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public double oceanRad;

        public void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] = Math.Max(data.vertHeight[i], oceanRad);
        }
    }
}
