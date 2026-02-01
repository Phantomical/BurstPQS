using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeightCurve))]
public class VertexHeightNoiseVertHeightCurve(PQSMod_VertexHeightNoiseVertHeightCurve mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeightCurve>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        var p = new Params
        {
            sphereRadiusMin = mod.sphere.radiusMin,
            heightStart = mod.heightStart,
            heightEnd = mod.heightEnd,
            deformity = mod.deformity,
        };

        var curve = new BurstAnimationCurve(mod.curve);

        if (mod.noiseMap is LibNoise.Perlin perlin)
            jobSet.Add(
                new BuildHeightsPerlinJob
                {
                    noise = new(perlin),
                    curve = curve,
                    p = p,
                }
            );
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            jobSet.Add(
                new BuildHeightsRidgedMultifractalJob
                {
                    noise = new(multi),
                    curve = curve,
                    p = p,
                }
            );
        else if (mod.noiseMap is LibNoise.Billow billow)
            jobSet.Add(
                new BuildHeightsBillowJob
                {
                    noise = new(billow),
                    curve = curve,
                    p = p,
                }
            );
        else
            jobSet.Add(
                new BuildHeightsFallbackJob
                {
                    noiseMap = mod.noiseMap,
                    curve = curve,
                    p = p,
                }
            );
    }

    struct Params
    {
        public double sphereRadiusMin;
        public float heightStart;
        public float heightEnd;
        public float deformity;
    }

    static void BuildVertex<N>(
        in BuildHeightsData data,
        in N noise,
        in BurstAnimationCurve curve,
        in Params p
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

    [BurstCompile]
    struct BuildHeightsPerlinJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstPerlin noise;
        public BurstAnimationCurve curve;
        public Params p;

        public void BuildHeights(in BuildHeightsData data) =>
            BuildVertex(in data, in noise, in curve, in p);

        public void Dispose() => curve.Dispose();
    }

    [BurstCompile]
    struct BuildHeightsRidgedMultifractalJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstRidgedMultifractal noise;
        public BurstAnimationCurve curve;
        public Params p;

        public void BuildHeights(in BuildHeightsData data) =>
            BuildVertex(in data, in noise, in curve, in p);

        public void Dispose() => curve.Dispose();
    }

    [BurstCompile]
    struct BuildHeightsBillowJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstBillow noise;
        public BurstAnimationCurve curve;
        public Params p;

        public void BuildHeights(in BuildHeightsData data) =>
            BuildVertex(in data, in noise, in curve, in p);

        public void Dispose() => curve.Dispose();
    }

    struct BuildHeightsFallbackJob : IBatchPQSHeightJob, IDisposable
    {
        public IModule noiseMap;
        public BurstAnimationCurve curve;
        public Params p;

        public void BuildHeights(in BuildHeightsData data) =>
            BuildVertex(in data, in noiseMap, in curve, in p);

        public void Dispose() => curve.Dispose();
    }
}
