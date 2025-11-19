using System;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class TangentTextureRanges : BatchPQSModV1<PQSMod_TangentTextureRanges>
{
    public TangentTextureRanges(PQSMod_TangentTextureRanges mod)
        : base(mod) { }

    public override unsafe void OnBatchVertexBuild(in QuadBuildDataV1 data)
    {
        fixed (float* pTangentX = PQSMod_TangentTextureRanges.tangentX)
        {
            BuildTangents(
                in data.burstData,
                new(pTangentX, PQSMod_TangentTextureRanges.tangentX.Length),
                mod.modulo,
                mod.lowStart,
                mod.lowEnd,
                mod.highStart,
                mod.highEnd
            );
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildTangents(
        [NoAlias] in BurstQuadBuildDataV1 data,
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

    static double SmoothStep(double a, double b, double x)
    {
        var t = MathUtil.Clamp01((x - a) / (b - a));
        return t * t * (3.0 - 2.0 * t);
    }
}
