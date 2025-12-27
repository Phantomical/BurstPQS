using System;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_FlattenOcean))]
public class FlattenOcean(PQSMod_FlattenOcean mod) : InlineBatchPQSMod<PQSMod_FlattenOcean>(mod)
{
    public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildHeightsJob { data = data.burst, oceanRad = mod.oceanRad };
        return job.Schedule(handle);
    }

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
