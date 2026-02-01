namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSROCControl))]
public class ROCControl(PQSROCControl mod) : BatchPQSMod<PQSROCControl>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { mod = mod });
    }

    struct BuildJob : IBatchPQSVertexJob, IBatchPQSMeshBuiltJob
    {
        public PQSROCControl mod;
        bool allowROCScatter;

        public void BuildVertices(in BuildVerticesData data)
        {
            allowROCScatter = data.allowScatter[data.VertexCount - 1];
        }

        public void OnMeshBuilt(PQ quad)
        {
            mod.allowROCScatter = allowROCScatter;
        }
    }
}
