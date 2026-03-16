using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexDefineCoastLine))]
public class VertexDefineCoastLine(PQSMod_VertexDefineCoastLine mod)
    : BatchPQSMod<PQSMod_VertexDefineCoastLine>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { oceanRadius = mod.oceanRadius, depthOffset = mod.depthOffset });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public double oceanRadius;
        public double depthOffset;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                if (data.vertHeight[i] < oceanRadius)
                    data.vertHeight[i] += depthOffset;
            }
        }
    }
}
