using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexHeightMap))]
public class VertexSimplexHeightMap(PQSMod_VertexSimplexHeightMap mod)
    : BatchPQSMod<PQSMod_VertexSimplexHeightMap>(mod)
{
    BurstSimplex simplex = new(mod.simplex);

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob
        {
            simplex = simplex,
            heightMap = new(mod.heightMap),
            heightStart = mod.heightStart,
            heightEnd = mod.heightEnd,
            deformity = mod.deformity,
        });
    }

    public override void Dispose()
    {
        simplex.Dispose();
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstSimplex simplex;
        public BurstMapSO heightMap;
        public double heightStart;
        public double heightEnd;
        public double deformity;

        public void BuildHeights(in BuildHeightsData data)
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

        public void Dispose()
        {
            heightMap.Dispose();
        }
    }
}
