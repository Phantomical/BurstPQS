using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_AltitudeAlpha))]
public class AltitudeAlpha(PQSMod_AltitudeAlpha mod)
    : BatchPQSMod<PQSMod_AltitudeAlpha>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle) => handle;

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildVerticesJob
        {
            data = data.burst,
            atmosphereDepth = mod.atmosphereDepth,
            invert = mod.invert,
        };

        return job.Schedule(handle);
    }

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public double atmosphereDepth;
        public bool invert;

        public void Execute()
        {
            if (invert)
            {
                for (int i = 0; i < data.VertexCount; ++i)
                {
                    double h = (data.vertHeight[i] - data.sphere.radius) / atmosphereDepth;
                    data.vertColor[i].a = (float)(1.0 - h);
                }
            }
            else
            {
                for (int i = 0; i < data.VertexCount; ++i)
                {
                    double h = (data.vertHeight[i] - data.sphere.radius) / atmosphereDepth;
                    data.vertColor[i].a = (float)h;
                }
            }
        }
    }
}
