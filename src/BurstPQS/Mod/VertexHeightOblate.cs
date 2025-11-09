using System;
using System.Windows.Markup;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Mathematics;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightOblate : BatchPQSMod<PQSMod_VertexHeightOblate>
{
    public VertexHeightOblate(PQSMod_VertexHeightOblate mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        BuildHeight(in data.burstData, mod.height, mod.pow);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildHeight(in BurstQuadBuildData data, double height, double pow)
    {
        int i = 0;
        for (; i + 4 <= data.VertexCount; i += 4)
        {
            double4 a = data.v.GetVec4(i);
            double4 h = data.vertHeight.GetVec4(i);

            a = math.sin(Math.PI * a);
            a = math.pow(a, pow);
            h += a * height;

            data.vertHeight.SetVec4(i, h);
        }

        for (; i < data.VertexCount; ++i)
        {
            double a;
            a = Math.Sin(Math.PI * data.v[i]);
            a = Math.Pow(a, pow);
            data.vertHeight[i] += a * height;
        }
    }
}
