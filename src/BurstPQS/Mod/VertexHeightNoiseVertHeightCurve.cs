using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightNoiseVertHeightCurve
    : PQSMod_VertexHeightNoiseVertHeightCurve,
        IBatchPQSMod
{
    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        var p = new Params
        {
            sphereRadiusMin = sphere.radiusMin,
            heightStart = heightStart,
            heightEnd = heightEnd,
            deformity = deformity,
        };

        using var guard = BurstAnimationCurve.Create(curve, out var bcurve);

        if (noiseMap is LibNoise.Perlin perlin)
            BuildVertexPerlin(in data.burstData, new(perlin), in bcurve, in p);
        else if (noiseMap is LibNoise.RidgedMultifractal multi)
            BuildVertexRidgedMultifractal(in data.burstData, new(multi), in bcurve, in p);
        else if (noiseMap is LibNoise.Billow billow)
            BuildVertexBillow(in data.burstData, new(billow), in bcurve, in p);
        else
            BuildVertex(in data.burstData, noiseMap, in bcurve, in p);
    }

    public void OnQuadBuildVertex(in QuadBuildData data) { }

    struct Params
    {
        public double sphereRadiusMin;
        public float heightStart;
        public float heightEnd;
        public float deformity;
    }

    static void BuildVertex<N>(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in N noise,
        [NoAlias] in BurstAnimationCurve curve,
        [NoAlias] in Params p
    )
        where N : IModule
    {
        double h;
        double n;
        float t;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            h = data.vertHeight[i] - p.sphereRadiusMin;
            if (h <= p.heightStart)
                t = 0f;
            else if (h >= p.heightEnd)
                t = 1f;
            else
                t = (float)((h - p.heightStart) / (p.heightEnd - p.heightStart));

            n = MathUtil.Clamp(noise.GetValue(data.directionFromCenter[i]), -1.0, 1.0);
            data.vertHeight[i] += (n + 1.0) * 0.5 * p.deformity * curve.Evaluate(t);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexPerlin(
        in BurstQuadBuildData data,
        in Perlin noise,
        in BurstAnimationCurve curve,
        in Params p
    ) => BuildVertex(in data, in noise, in curve, in p);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        in BurstQuadBuildData data,
        in RidgedMultifractal noise,
        in BurstAnimationCurve curve,
        in Params p
    ) => BuildVertex(in data, in noise, in curve, in p);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(
        in BurstQuadBuildData data,
        in Billow noise,
        in BurstAnimationCurve curve,
        in Params p
    ) => BuildVertex(in data, in noise, in curve, in p);
}
