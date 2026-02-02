using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_SmoothLatitudeRange))]
public class SmoothLatitudeRange(PQSMod_SmoothLatitudeRange mod)
    : BatchPQSMod<PQSMod_SmoothLatitudeRange>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                latitudeRange = new(mod.latitudeRange),
                smoothToAltitude = mod.smoothToAltitude,
                sphereRadius = mod.sphere.radius,
            }
        );
    }

    // [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public BurstLerpRange latitudeRange;
        public double smoothToAltitude;
        public double sphereRadius;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var smooth = latitudeRange.Lerp(data.sy[i]);
                if (smooth == 0.0)
                    continue;

                var alt = data.vertHeight[i] - sphereRadius;
                var result = alt * (1.0 - smooth) + smoothToAltitude * smooth;
                data.vertHeight[i] = sphereRadius + result;
            }
        }
    }
}
