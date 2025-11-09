using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexSimplexNoiseColor : PQSMod_VertexSimplexNoiseColor, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        using var g0 = BurstSimplex.Create(simplex, out var bsimplex);

        BuildVertices(in data.burstData, in bsimplex, colorStart, colorEnd, blend);
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstSimplex simplex,
        in Color iColorStart,
        in Color iColorEnd,
        float blend
    )
    {
        Color colorStart = iColorStart;
        Color colorEnd = iColorEnd;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            var dir = data.directionFromCenter[i];
            var n = (float)((simplex.noise(dir) + 1.0) * 0.5);
            var c = Color.Lerp(colorStart, colorEnd, n);

            data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
        }
    }
}
