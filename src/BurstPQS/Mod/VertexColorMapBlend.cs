using System;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorMapBlend))]
public class VertexColorMapBlend(PQSMod_VertexColorMapBlend mod)
    : BatchPQSMod<PQSMod_VertexColorMapBlend>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildVerticesJob { vertexColorMap = new(mod.vertexColorMap), blend = mod.blend }
        );
    }

    [BurstCompile]
    struct BuildVerticesJob : IBatchPQSVertexJob, IDisposable
    {
        public BurstMapSO vertexColorMap;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertColor[i] = Color.Lerp(
                    data.vertColor[i],
                    vertexColorMap.GetPixelColor(data.u[i], data.v[i]),
                    blend
                );
            }
        }

        public void Dispose()
        {
            vertexColorMap.Dispose();
        }
    }
}
