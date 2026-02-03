using System;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_FlattenOcean))]
public class FlattenOcean(PQSMod_FlattenOcean mod) : BatchPQSMod<PQSMod_FlattenOcean>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { oceanRad = mod.oceanRad });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public double oceanRad;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] = Math.Max(data.vertHeight[i], oceanRad);
        }
    }
}
