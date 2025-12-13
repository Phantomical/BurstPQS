using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_AltitudeUV))]
[BatchPQSShim]
public class AltitudeUV(PQSMod_AltitudeUV mod)
    : BatchPQSMod<PQSMod_AltitudeUV>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildVerticesJob
        {
            data = data.burst,
            radius = mod.sphere.radius,
            atmosphereHeight = mod.atmosphereHeight,
            oceanDepth = mod.oceanDepth,
            invert = mod.invert,
        };

        return job.Schedule(handle);
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public double radius;
        public double atmosphereHeight;
        public double oceanDepth;
        public bool invert;

        public readonly void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = data.vertHeight[i] - radius;
                if (h >= 0.0)
                    h /= atmosphereHeight;
                else
                    h /= oceanDepth;
                h = MathUtil.Clamp(h, -1.0, 1.0);

                if (invert)
                    h = 1.0 - h;

                data.u3[i] = h;
            }
        }
    }
}
