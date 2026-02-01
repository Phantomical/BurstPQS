using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_GnomonicTest))]
public class GnomonicTest(PQSMod_GnomonicTest mod) : BatchPQSMod<PQSMod_GnomonicTest>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob());
    }

    struct BuildJob : IBatchPQSVertexJob
    {
        public readonly void BuildVertices(in BuildVerticesData data)
        {
            // TODO: gnomonicUVs not available in BuildVerticesData
        }
    }
}
