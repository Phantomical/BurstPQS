using Unity.Burst;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightOffset))]
public class VertexHeightOffset(PQSMod_VertexHeightOffset mod)
    : BatchPQSMod<PQSMod_VertexHeightOffset>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { offset = mod.offset });
    }

    // [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public double offset;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += offset;
        }
    }
}
