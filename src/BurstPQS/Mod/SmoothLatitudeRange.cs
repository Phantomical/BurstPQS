using System;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class SmoothLatitudeRange : PQSMod_SmoothLatitudeRange, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        BuildVertexHeight(in data.burstData, new(latitudeRange), smoothToAltitude, sphere.radius);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexHeight(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstLerpRange latitudeRange,
        double smoothToAltitude,
        double sphereRadius
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var smooth = latitudeRange.Lerp(data.sy[i]);
            if (smooth == 0.0)
                return;

            var alt = data.vertHeight[i] - sphereRadius;
            var result = alt * (1.0 - smooth) + smoothToAltitude * smooth;
            data.vertHeight[i] = sphereRadius + result;
        }
    }
}
