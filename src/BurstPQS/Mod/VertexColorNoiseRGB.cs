using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorNoiseRGB : BatchPQSModV1<PQSMod_VertexColorNoiseRGB>
{
    public VertexColorNoiseRGB(PQSMod_VertexColorNoiseRGB mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildDataV1 data)
    {
        var blend = new Blends
        {
            r = mod.rBlend,
            g = mod.gBlend,
            b = mod.bBlend,
            total = mod.blend,
        };

        if (mod.noiseMap is LibNoise.Perlin perlin)
            BuildVertexPerlin(in data.burstData, new(perlin), blend);
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            BuildVertexRidgedMultifractal(in data.burstData, new(multi), blend);
        else if (mod.noiseMap is LibNoise.Billow billow)
            BuildVertexBillow(in data.burstData, new(billow), blend);
        else
            BuildVertex(in data.burstData, mod.noiseMap, blend);
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
    static void BuildVertexPerlin(in BurstQuadBuildDataV1 data, in Perlin noise, in Blends blend) =>
        BuildVertex(in data, in noise, in blend);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        in BurstQuadBuildDataV1 data,
        in RidgedMultifractal noise,
        in Blends blend
    ) => BuildVertex(in data, in noise, in blend);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(in BurstQuadBuildDataV1 data, in Billow noise, in Blends blend) =>
        BuildVertex(in data, in noise, in blend);

    static void BuildVertex<N>(
        [NoAlias] in BurstQuadBuildDataV1 data,
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
