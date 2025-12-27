using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorMap))]
public class VertexColorMap(PQSMod_VertexColorMap mod)
    : InlineBatchPQSMod<PQSMod_VertexColorMap>(mod)
{
    public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
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
