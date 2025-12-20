using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeight))]
public class VertexHeightNoiseVertHeight(PQSMod_VertexHeightNoiseVertHeight mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeight>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var p = new BuildHeightsData
        {
            data = data.burst,
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
                var pjob = new BuildHeightsPerlinJob { data = p, noise = new(perlin) };
                handle = pjob.Schedule(handle);
                break;

            case LibNoise.RidgedMultifractal multi:
                var mjob = new BuildHeightsRidgedMultifractalJob { data = p, noise = new(multi) };
                handle = mjob.Schedule(handle);
                break;

            case LibNoise.Billow billow:
                var bjob = new BuildHeightsBillowJob { data = p, noise = new(billow) };
                handle = bjob.Schedule(handle);
                break;

            default:
                handle.Complete();
                p.Execute(mod.noiseMap);
                break;
        }

        return handle;
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    struct BuildHeightsData
    {
        public BurstQuadBuildData data;
        public double sphereRadiusMin;
        public double sphereRadiusDelta;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public void Execute<N>(in N noise)
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

    [BurstCompile]
    struct BuildHeightsPerlinJob : IJob
    {
        public BuildHeightsData data;
        public BurstPerlin noise;

        public void Execute()
        {
            data.Execute(in noise);
        }
    }

    [BurstCompile]
    struct BuildHeightsRidgedMultifractalJob : IJob
    {
        public BuildHeightsData data;
        public BurstRidgedMultifractal noise;

        public void Execute()
        {
            data.Execute(in noise);
        }
    }

    [BurstCompile]
    struct BuildHeightsBillowJob : IJob
    {
        public BuildHeightsData data;
        public BurstBillow noise;

        public void Execute()
        {
            data.Execute(in noise);
        }
    }
}
