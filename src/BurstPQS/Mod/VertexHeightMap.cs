using System;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightMap))]
public class VertexHeightMap(PQSMod_VertexHeightMap mod)
    : BatchPQSMod<PQSMod_VertexHeightMap>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildHeightsJob
        {
            heightMap = new BurstMapSO(mod.heightMap),
            heightMapOffset = mod.heightMapOffset,
            heightMapDeformity = mod.heightMapDeformity,
        });
    }

    [BurstCompile]
    struct BuildHeightsJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstMapSO heightMap;
        public double heightMapOffset;
        public double heightMapDeformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertHeight[i] +=
                    heightMapOffset
                    + heightMapDeformity * heightMap.GetPixelFloat(data.u[i], data.v[i]);
            }
        }

        public void Dispose()
        {
            heightMap.Dispose();
        }
    }
}
