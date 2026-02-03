using BurstPQS.Noise;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoise))]
public class VertexHeightNoise(PQSMod_VertexHeightNoise mod)
    : BatchPQSMod<PQSMod_VertexHeightNoise>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        switch (mod.noiseMap)
        {
            case LibNoise.Perlin perlin:
                jobSet.Add(
                    new BuildHeightsPerlin { deformity = mod.deformity, noise = new(perlin) }
                );
                break;

            case LibNoise.RidgedMultifractal multi:
                jobSet.Add(
                    new BuildHeightsRidgedMultifractal
                    {
                        deformity = mod.deformity,
                        noise = new(multi),
                    }
                );
                break;

            case LibNoise.Billow billow:
                jobSet.Add(
                    new BuildHeightsBillow { deformity = mod.deformity, noise = new(billow) }
                );
                break;

            default:
                jobSet.Add(
                    new BuildHeightsFallback { deformity = mod.deformity, noiseMap = mod.noiseMap }
                );
                break;
        }
    }

    static void Execute<N>(in N noise, double deformity, in BuildHeightsData data)
        where N : IModule
    {
        for (int i = 0; i < data.VertexCount; ++i)
            data.vertHeight[i] += noise.GetValue(data.directionFromCenter[i]) * deformity;
    }

    [BurstCompile]
    struct BuildHeightsPerlin : IBatchPQSHeightJob
    {
        public double deformity;
        public BurstPerlin noise;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            Execute(in noise, deformity, in data);
    }

    [BurstCompile]
    struct BuildHeightsRidgedMultifractal : IBatchPQSHeightJob
    {
        public double deformity;
        public BurstRidgedMultifractal noise;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            Execute(in noise, deformity, in data);
    }

    [BurstCompile]
    struct BuildHeightsBillow : IBatchPQSHeightJob
    {
        public double deformity;
        public BurstBillow noise;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            Execute(in noise, deformity, in data);
    }

    struct BuildHeightsFallback : IBatchPQSHeightJob
    {
        public double deformity;
        public IModule noiseMap;

        public readonly void BuildHeights(in BuildHeightsData data) =>
            Execute(in noiseMap, deformity, in data);
    }
}
