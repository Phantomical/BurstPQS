using System;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorMap))]
public class VertexColorMap(PQSMod_VertexColorMap mod) : BatchPQSMod<PQSMod_VertexColorMap>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildVerticesJob { vertexColorMap = new(mod.vertexColorMap) });
    }

    [BurstCompile]
    struct BuildVerticesJob : IBatchPQSVertexJob, IDisposable
    {
        public BurstMapSO vertexColorMap;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertColor[i] = vertexColorMap.GetPixelColor(data.u[i], data.v[i]);
            }
        }

        public void Dispose()
        {
            vertexColorMap.Dispose();
        }
    }
}
