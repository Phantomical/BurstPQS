using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeight))]
public class VertexHeightNoiseVertHeight(PQSMod_VertexHeightNoiseVertHeight mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeight>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        var p = new HeightParams
        {
            sphereRadiusMin = mod.sphere.radiusMin,
            sphereRadiusDelta = mod.sphere.radiusDelta,
            heightStart = mod.heightStart,
            heightEnd = mod.heightEnd,
            hDeltaR = mod.hDeltaR,
            deformity = mod.deformity,
        };

        switch (mod.noiseMap)
        {
            case LibNoise.Perlin perlin:
                jobSet.Add(new BuildHeightsPerlinJob { p = p, noise = new(perlin) });
                break;

            case LibNoise.RidgedMultifractal multi:
                jobSet.Add(new BuildHeightsRidgedMultifractalJob { p = p, noise = new(multi) });
                break;

            case LibNoise.Billow billow:
                jobSet.Add(new BuildHeightsBillowJob { p = p, noise = new(billow) });
                break;

            default:
                jobSet.Add(new BuildHeightsFallbackJob { p = p, noiseMap = mod.noiseMap });
                break;
        }
    }

    struct HeightParams
    {
        public double sphereRadiusMin;
        public double sphereRadiusDelta;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public void Execute<N>(in BuildHeightsData data, in N noise)
            where N : IModule
        {
            double h;
            double n;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                h = (data.vertHeight[i] - sphereRadiusMin) / sphereRadiusDelta;
                if (h < heightStart || h > heightEnd)
                    continue;
                h = (h - heightStart) * hDeltaR;
                n = MathUtil.Clamp(noise.GetValue(data.directionFromCenter[i]), -1.0, 1.0);

                data.vertHeight[i] += (n + 1.0) * 0.5 * deformity * h;
            }
        }
    }

    // [BurstCompile]
    struct BuildHeightsPerlinJob : IBatchPQSHeightJob
    {
        public HeightParams p;
        public BurstPerlin noise;

        public void BuildHeights(in BuildHeightsData data)
        {
            p.Execute(in data, in noise);
        }
    }

    // [BurstCompile]
    struct BuildHeightsRidgedMultifractalJob : IBatchPQSHeightJob
    {
        public HeightParams p;
        public BurstRidgedMultifractal noise;

        public void BuildHeights(in BuildHeightsData data)
        {
            p.Execute(in data, in noise);
        }
    }

    // [BurstCompile]
    struct BuildHeightsBillowJob : IBatchPQSHeightJob
    {
        public HeightParams p;
        public BurstBillow noise;

        public void BuildHeights(in BuildHeightsData data)
        {
            p.Execute(in data, in noise);
        }
    }

    struct BuildHeightsFallbackJob : IBatchPQSHeightJob
    {
        public HeightParams p;
        public IModule noiseMap;

        public void BuildHeights(in BuildHeightsData data)
        {
            p.Execute(in data, in noiseMap);
        }
    }
}
