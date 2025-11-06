using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorNoise : PQSMod_VertexColorNoise, IBatchPQSMod
{
    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        if (noiseMap is LibNoise.Perlin perlin)
            BuildVertexPerlin(in data.burstData, new(perlin), blend);
        else if (noiseMap is LibNoise.RidgedMultifractal multi)
            BuildVertexRidgedMultifractal(in data.burstData, new(multi), blend);
        else if (noiseMap is LibNoise.Billow billow)
            BuildVertexBillow(in data.burstData, new(billow), blend);
        else
            BuildVertex(in data.burstData, noiseMap, blend);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexPerlin(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in Perlin noise,
        float blend
    ) => BuildVertex(in data, in noise, blend);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in RidgedMultifractal noise,
        float blend
    ) => BuildVertex(in data, in noise, blend);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in Billow noise,
        float blend
    ) => BuildVertex(in data, in noise, blend);

    static void BuildVertex<N>(in BurstQuadBuildData data, in N noise, float blend)
        where N : IModule
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var h = (float)((noise.GetValue(data.directionFromCenter[i]) + 1.0) * 0.5);
            var c = new Color(h, h, h, 1f);
            data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
        }
    }
}
