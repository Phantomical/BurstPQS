using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class AltitudeAlpha : PQSMod_AltitudeAlpha, IBatchPQSMod
{
    public AltitudeAlpha(PQSMod_AltitudeAlpha mod)
    {
        CloneUtil.MemberwiseCopy(mod, this);
    }

    public virtual void OnQuadBuildVertex(in QuadBuildData data)
    {
        BuildVertices(in data.burstData, sphere.radius, atmosphereDepth, invert);
    }

    public virtual void OnQuadBuildVertexHeight(in QuadBuildData data) { }

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
