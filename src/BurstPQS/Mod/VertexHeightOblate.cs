using System;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightOblate : PQSMod_VertexHeightOblate, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        BuildHeight(in data.burstData, height, pow);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildHeight(in BurstQuadBuildData data, double height, double pow)
    {
        double a;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            a = Math.Sin(Math.PI * data.v[i]);
            a = Math.Pow(a, pow);
            data.vertHeight[i] += a * height;
        }
    }
}
