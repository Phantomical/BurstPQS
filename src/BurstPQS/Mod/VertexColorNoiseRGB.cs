using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorNoiseRGB : PQSMod_VertexColorNoiseRGB, IBatchPQSMod
{
    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        var blend = new Blends
        {
            r = rBlend,
            g = gBlend,
            b = bBlend,
            total = this.blend,
        };

        if (noiseMap is LibNoise.Perlin perlin)
            BuildVertexPerlin(in data.burstData, new(perlin), blend);
        else if (noiseMap is LibNoise.RidgedMultifractal multi)
            BuildVertexRidgedMultifractal(in data.burstData, new(multi), blend);
        else if (noiseMap is LibNoise.Billow billow)
            BuildVertexBillow(in data.burstData, new(billow), blend);
        else
            BuildVertex(in data.burstData, noiseMap, blend);
    }

    struct Blends
    {
        public float r;
        public float g;
        public float b;
        public float total;
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexPerlin(in BurstQuadBuildData data, in Perlin noise, in Blends blend) =>
        BuildVertex(in data, in noise, in blend);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        in BurstQuadBuildData data,
        in RidgedMultifractal noise,
        in Blends blend
    ) => BuildVertex(in data, in noise, in blend);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(in BurstQuadBuildData data, in Billow noise, in Blends blend) =>
        BuildVertex(in data, in noise, in blend);

    static void BuildVertex<N>(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in N noise,
        in Blends blend
    )
        where N : IModule
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var h = (float)((noise.GetValue(data.directionFromCenter[i]) + 1.0) * 0.5);
            var c = new Color(h * blend.r, h * blend.g, h * blend.b, 1f);
            data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend.total);
        }
    }
}
