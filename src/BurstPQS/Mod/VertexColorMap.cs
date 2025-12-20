using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorMap))]
public class VertexColorMap(PQSMod_VertexColorMap mod)
    : BatchPQSMod<PQSMod_VertexColorMap>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildVerticesJob
        {
            data = data.burst,
            vertexColorMap = new(mod.vertexColorMap),
        };

        handle = job.Schedule(handle);
        job.vertexColorMap.Dispose(handle);

        return handle;
    }

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstMapSO vertexColorMap;

        public void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertColor[i] = vertexColorMap.GetPixelColor(data.u[i], data.v[i]);
            }
        }
    }
}
