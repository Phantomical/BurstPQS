using System;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class TangentTextureRanges : PQSMod_TangentTextureRanges, IBatchPQSMod
{
    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    public unsafe void OnQuadBuildVertex(in QuadBuildData data)
    {
        fixed (float* pTangentX = tangentX)
        {
            BuildTangents(
                in data.burstData,
                new(pTangentX, tangentX.Length),
                modulo,
                lowStart,
                lowEnd,
                highStart,
                highEnd
            );
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildTangents(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in MemorySpan<float> tangentX,
        double modulo,
        double lowStart,
        double lowEnd,
        double highStart,
        double highEnd
    )
    {
        if (tangentX.Length < data.VertexCount)
            BurstException.ThrowIndexOutOfRange();

        for (int i = 0; i < data.VertexCount; ++i)
        {
            var height = data.vertHeight[i];
            var low = 1.0 - SmoothStep(lowStart, lowEnd, height);
            var high = SmoothStep(highStart, highEnd, height);
            var med = 1.0 - low - high;

            low = Math.Round(low * modulo);
            med = Math.Round(med * modulo) * 2.0;
            high = Math.Round(high * modulo) * 3.0;

            tangentX[i] = (float)(high + med + low);
        }
    }

    static new double SmoothStep(double a, double b, double x)
    {
        var t = MathUtil.Clamp01((x - a) / (b - a));
        return t * t * (3.0 - 2.0 * t);
    }
}
