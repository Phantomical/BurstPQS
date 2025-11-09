using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class AltitudeAlpha : BatchPQSMod<PQSMod_AltitudeAlpha>
{
    public AltitudeAlpha(PQSMod_AltitudeAlpha mod)
        : base(mod) { }

    public override void OnQuadBuildVertex(in QuadBuildData data)
    {
        BuildVertices(in data.burstData, mod.sphere.radius, mod.atmosphereDepth, mod.invert);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        double radius,
        double atmosphereDepth,
        bool invert
    )
    {
        if (invert)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = (data.vertHeight[i] - radius) / atmosphereDepth;
                data.vertColor[i].a = (float)(1.0 - h);
            }
        }
        else
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = (data.vertHeight[i] - radius) / atmosphereDepth;
                data.vertColor[i].a = (float)h;
            }
        }
    }
}
