using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

public class AltitudeUV : PQSMod_AltitudeUV, IBatchPQSMod
{
    public AltitudeUV(PQSMod_AltitudeUV mod)
    {
        CloneUtil.MemberwiseCopy(mod, this);
    }

    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        SetUVs(in data.burstData, sphere.radius, atmosphereHeight, oceanDepth, invert);
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void SetUVs(
        in BurstQuadBuildData data,
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
            h = UtilMath.Clamp(h, -1.0, 1.0);

            if (invert)
                h = 1.0 - h;

            data.u3[i] = h;
        }
    }
}
