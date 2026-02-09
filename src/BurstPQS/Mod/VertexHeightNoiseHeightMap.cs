using BurstPQS.Map;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseHeightMap))]
public class VertexHeightNoiseHeightMap(PQSMod_VertexHeightNoiseHeightMap mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseHeightMap>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        switch (mod.noiseMap)
        {
            case LibNoise.Perlin perlin:
                jobSet.Add(
                    new BuildJobPerlin
                    {
                        heightMap = TextureMapSO.Create(mod.heightMap, MapSO.MapDepth.RGB),
                        noise = new(perlin),
                        heightStart = mod.heightStart,
                        heightEnd = mod.heightEnd,
                        hDeltaR = mod.hDeltaR,
                        deformity = mod.deformity,
                    }
                );
                break;

            case LibNoise.RidgedMultifractal multi:
                jobSet.Add(
                    new BuildJobRidgedMultifractal
                    {
                        heightMap = TextureMapSO.Create(mod.heightMap, MapSO.MapDepth.RGB),
                        noise = new(multi),
                        heightStart = mod.heightStart,
                        heightEnd = mod.heightEnd,
                        hDeltaR = mod.hDeltaR,
                        deformity = mod.deformity,
                    }
                );
                break;

            case LibNoise.Billow billow:
                jobSet.Add(
                    new BuildJobBillow
                    {
                        heightMap = TextureMapSO.Create(mod.heightMap, MapSO.MapDepth.RGB),
                        noise = new(billow),
                        heightStart = mod.heightStart,
                        heightEnd = mod.heightEnd,
                        hDeltaR = mod.hDeltaR,
                        deformity = mod.deformity,
                    }
                );
                break;

            default:
                jobSet.Add(
                    new BuildJobFallback
                    {
                        heightMap = TextureMapSO.Create(mod.heightMap, MapSO.MapDepth.RGB),
                        noiseMap = mod.noiseMap,
                        heightStart = mod.heightStart,
                        heightEnd = mod.heightEnd,
                        hDeltaR = mod.hDeltaR,
                        deformity = mod.deformity,
                    }
                );
                break;
        }
    }

    static void BuildHeightsImpl<N>(
        in BuildHeightsData data,
        BurstMapSO heightMap,
        in N noise,
        float heightStart,
        float heightEnd,
        double hDeltaR,
        float deformity
    )
        where N : IModule
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var c = heightMap.GetPixelColor((float)data.sx[i], (float)data.sy[i]);
            double h = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            if (h < heightStart || h > heightEnd)
                continue;

            h = (h - heightStart) * hDeltaR;
            double n = MathUtil.Clamp(noise.GetValue(data.directionFromCenter[i]), -1d, 1d);

            data.vertHeight[i] += (n + 1.0) * 0.5 * deformity * h;
        }
    }

    // [BurstCompile]
    struct BuildJobPerlin : IBatchPQSHeightJob
    {
        public BurstMapSO heightMap;
        public BurstPerlin noise;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            BuildHeightsImpl(
                in data,
                heightMap,
                in noise,
                heightStart,
                heightEnd,
                hDeltaR,
                deformity
            );
    }

    struct BuildJobRidgedMultifractal : IBatchPQSHeightJob
    {
        public BurstMapSO heightMap;
        public BurstRidgedMultifractal noise;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            BuildHeightsImpl(
                in data,
                heightMap,
                in noise,
                heightStart,
                heightEnd,
                hDeltaR,
                deformity
            );
    }

    struct BuildJobBillow : IBatchPQSHeightJob
    {
        public BurstMapSO heightMap;
        public BurstBillow noise;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            BuildHeightsImpl(
                in data,
                heightMap,
                in noise,
                heightStart,
                heightEnd,
                hDeltaR,
                deformity
            );
    }

    struct BuildJobFallback : IBatchPQSHeightJob
    {
        public BurstMapSO heightMap;
        public IModule noiseMap;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            BuildHeightsImpl(
                in data,
                heightMap,
                noiseMap,
                heightStart,
                heightEnd,
                hDeltaR,
                deformity
            );
    }
}
