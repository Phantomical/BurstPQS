using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeightMap))]
public class VertexSimplexHeightMap(PQSMod_VertexSimplexHeightMap mod)
    : InlineBatchPQSMod<PQSMod_VertexSimplexHeightMap>(mod)
{
    BurstSimplex simplex = new(mod.simplex);

    public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildHeightsJob
        {
            data = data.burst,
            simplex = simplex,
            heightMap = new(mod.heightMap),
            heightStart = mod.heightStart,
            heightEnd = mod.heightEnd,
            deformity = mod.deformity,
        };

        handle = job.Schedule(handle);
        job.heightMap.Dispose(handle);

        return handle;
    }

    public override void Dispose()
    {
        simplex.Dispose();
    }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstSimplex simplex;
        public BurstMapSO heightMap;
        public double heightStart;
        public double heightEnd;
        public double deformity;

        public void Execute()
        {
            double hDeltaR = 1.0 / (heightEnd - heightStart);

            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = heightMap.GetPixelFloat(data.u[i], data.v[i]);
                if (h < heightStart || h > heightEnd)
                    continue;

                h = (h - heightStart) * hDeltaR;
                data.vertHeight[i] +=
                    (simplex.noise(data.directionFromCenter[i]) + 1.0) * 0.5 * deformity * h;
            }
        }
    }
}
