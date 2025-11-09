using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile(FloatMode = FloatMode.Fast)]
public class VertexHeightNoiseVertHeight : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeight>
{
    public VertexHeightNoiseVertHeight(PQSMod_VertexHeightNoiseVertHeight mod)
        : base(mod) { }

    public override void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        var p = new Params
        {
            sphereRadiusMin = mod.sphere.radiusMin,
            sphereRadiusDelta = mod.sphere.radiusDelta,
            heightStart = mod.heightStart,
            heightEnd = mod.heightEnd,
            hDeltaR = mod.hDeltaR,
            deformity = mod.deformity,
        };

        if (mod.noiseMap is LibNoise.Perlin perlin)
            BuildVertexPerlin(in data.burstData, new(perlin), in p);
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            BuildVertexRidgedMultifractal(in data.burstData, new(multi), in p);
        else if (mod.noiseMap is LibNoise.Billow billow)
            BuildVertexBillow(in data.burstData, new(billow), in p);
        else
            BuildVertex(in data.burstData, mod.noiseMap, in p);
    }

    struct Params
    {
        public double sphereRadiusMin;
        public double sphereRadiusDelta;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;
    }

    static void BuildVertex<N>(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in N noise,
        [NoAlias] in Params p
    )
        where N : IModule
    {
        double h;
        double n;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            h = (data.vertHeight[i] - p.sphereRadiusMin) / p.sphereRadiusDelta;
            if (h < p.heightStart || h > p.heightEnd)
                continue;
            h = (h - p.heightStart) * p.hDeltaR;
            n = MathUtil.Clamp(noise.GetValue(data.directionFromCenter[i]), -1.0, 1.0);

            data.vertHeight[i] += (n + 1.0) * 0.5 * p.deformity * h;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexPerlin(in BurstQuadBuildData data, in Perlin noise, in Params p) =>
        BuildVertex(in data, in noise, p);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        in BurstQuadBuildData data,
        in RidgedMultifractal noise,
        in Params p
    ) => BuildVertex(in data, in noise, p);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(in BurstQuadBuildData data, in Billow noise, in Params p) =>
        BuildVertex(in data, in noise, p);
}
