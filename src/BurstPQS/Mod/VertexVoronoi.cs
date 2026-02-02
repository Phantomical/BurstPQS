using BurstPQS.Noise;
using Unity.Burst;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexVoronoi))]
public class VertexVoronoi(PQSMod_VertexVoronoi mod) : BatchPQSMod<PQSMod_VertexVoronoi>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildHeightsJob { voronoi = new(mod.voronoi), deformation = mod.deformation }
        );
    }

    // [BurstCompile]
    struct BuildHeightsJob : IBatchPQSHeightJob
    {
        public BurstVoronoi voronoi;
        public double deformation;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += voronoi.GetValue(data.directionFromCenter[i]) * deformation;
        }
    }
}
