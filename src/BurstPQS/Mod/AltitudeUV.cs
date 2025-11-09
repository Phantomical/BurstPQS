using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class AltitudeUV : BatchPQSMod<PQSMod_AltitudeUV>
{
    public AltitudeUV(PQSMod_AltitudeUV mod)
        : base(mod) { }

    public override void OnQuadBuildVertex(in QuadBuildData data)
    {
        BuildVertices(
            in data.burstData,
            mod.sphere.radius,
            mod.atmosphereHeight,
            mod.oceanDepth,
            mod.invert
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        double radius,
        double atmosphereHeight,
        double oceanDepth,
        bool invert
    )
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
