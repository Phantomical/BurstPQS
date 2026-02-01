using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_AltitudeUV))]
public class AltitudeUV(PQSMod_AltitudeUV mod) : BatchPQSMod<PQSMod_AltitudeUV>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                radius = mod.sphere.radius,
                atmosphereHeight = mod.atmosphereHeight,
                oceanDepth = mod.oceanDepth,
                invert = mod.invert,
            }
        );
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob
    {
        public double radius;
        public double atmosphereHeight;
        public double oceanDepth;
        public bool invert;

        public readonly void BuildVertices(in BuildVerticesData data)
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
