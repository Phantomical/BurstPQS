using Unity.Jobs;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSROCControl))]
public class ROCControl(PQSROCControl mod) : BatchPQSMod<PQSROCControl>(mod), IBatchPQSModState
{
    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data)
    {
        mod.OnQuadPreBuild(data.buildQuad);
        return this;
    }

    public void OnQuadBuilt(QuadBuildData data)
    {
        mod.allowROCScatter = data.allowScatter[data.VertexCount - 1];
        mod.OnQuadBuilt(data.buildQuad);
    }

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle) => handle;

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;
}
