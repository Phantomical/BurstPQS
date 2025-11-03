using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class AltitudeAlpha : PQSMod_AltitudeAlpha, IBatchPQSMod
{
    public AltitudeAlpha(PQSMod_AltitudeAlpha mod)
    {
        CloneUtil.MemberwiseCopyTo(mod, this);
    }

    public override void OnSetup()
    {
        SetAlphasFunc ??= BurstCompiler.CompileFunctionPointer<SetAlphasDelegate>(SetAlphas).Invoke;
        base.OnSetup();
    }

    public virtual void OnQuadBuildVertex(in QuadBuildData data)
    {
        SetAlphasFunc(data.vertColor, data.vertHeight, sphere.radius, atmosphereDepth, invert);
    }

    public virtual void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    delegate void SetAlphasDelegate(
        in MemorySpan<Color> colors,
        in MemorySpan<double> heights,
        double radius,
        double atmosphereDepth,
        bool invert
    );
    static SetAlphasDelegate SetAlphasFunc = null;

    [BurstCompile]
    static void SetAlphas(
        in MemorySpan<Color> colors,
        in MemorySpan<double> heights,
        double radius,
        double atmosphereDepth,
        bool invert
    )
    {
        if (colors.Length != heights.Length)
            return;

        if (invert)
        {
            for (int i = 0; i < colors.Length; ++i)
            {
                double h = (heights[i] - radius) / atmosphereDepth;
                colors[i].a = (float)(1.0 - h);
            }
        }
        else
        {
            for (int i = 0; i < colors.Length; ++i)
            {
                double h = (heights[i] - radius) / atmosphereDepth;
                colors[i].a = (float)h;
            }
        }
    }
}
