using BurstPQS.Noise;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexVoronoi))]
public class VertexVoronoi(PQSMod_VertexVoronoi mod)
    : InlineBatchPQSMod<PQSMod_VertexVoronoi>(mod),
        IBatchPQSModState
{
    public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildHeightsJob
        {
            data = data.burst,
            voronoi = new(mod.voronoi),
            deformation = mod.deformation,
        };

        return job.Schedule(handle);
    }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstVoronoi voronoi;
        public double deformation;

        public void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += voronoi.GetValue(data.directionFromCenter[i]) * deformation;
        }
    }
}
