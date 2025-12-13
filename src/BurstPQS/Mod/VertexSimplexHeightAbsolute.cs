using BurstPQS.Noise;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeightAbsolute))]
[BatchPQSShim]
public class VertexSimplexHeightAbsolute(PQSMod_VertexSimplexHeightAbsolute mod)
    : BatchPQSMod<PQSMod_VertexSimplexHeightAbsolute>(mod),
        IBatchPQSModState
{
    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data) => this;

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var bsimplex = new BurstSimplex(mod.simplex);
        var job = new BuildHeightsJob
        {
            data = data.burst,
            simplex = bsimplex,
            deformity = mod.deformity,
        };

        handle = job.Schedule(handle);
        bsimplex.Dispose(handle);

        return handle;
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstSimplex simplex;
        public double deformity;

        public void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertHeight[i] +=
                    (simplex.noise(data.directionFromCenter[i]) + 1.0) * 0.5 * deformity;
            }
        }
    }
}
