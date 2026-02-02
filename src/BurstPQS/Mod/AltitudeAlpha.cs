using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_AltitudeAlpha))]
public class AltitudeAlpha(PQSMod_AltitudeAlpha mod) : BatchPQSMod<PQSMod_AltitudeAlpha>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { atmosphereDepth = mod.atmosphereDepth, invert = mod.invert });
    }

    // [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob
    {
        public double atmosphereDepth;
        public bool invert;

        public readonly void BuildVertices(in BuildVerticesData data)
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
