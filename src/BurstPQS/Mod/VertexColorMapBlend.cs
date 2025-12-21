using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorMapBlend))]
public class VertexColorMapBlend(PQSMod_VertexColorMapBlend mod)
    : BatchPQSMod<PQSMod_VertexColorMapBlend>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle) => handle;

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildVerticesJob
        {
            data = data.burst,
            vertexColorMap = new(mod.vertexColorMap),
            blend = mod.blend,
        };

        handle = job.Schedule(handle);
        job.vertexColorMap.Dispose(handle);

        return handle;
    }

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstMapSO vertexColorMap;
        public float blend;

        public void Execute()
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
    }
}
