using BurstPQS.CompilerServices;
using BurstPQS.Noise;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoise))]
public partial class VertexHeightNoise(PQSMod_VertexHeightNoise mod)
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
                    new BuildHeightsFallback { deformity = mod.deformity, noise = mod.noiseMap }
                );
                break;
        }
    }

    struct BuildHeightsBase<N>
        where N : IModule
    {
        public double deformity;
        public N noise;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += noise.GetValue(data.directionFromCenter[i]) * deformity;
        }
    }

    [StructInherit(typeof(BuildHeightsBase<BurstPerlin>))]
    [BurstCompile]
    partial struct BuildHeightsPerlin : IBatchPQSHeightJob { }

    [StructInherit(typeof(BuildHeightsBase<BurstRidgedMultifractal>))]
    [BurstCompile]
    partial struct BuildHeightsRidgedMultifractal : IBatchPQSHeightJob { }

    [StructInherit(typeof(BuildHeightsBase<BurstBillow>))]
    [BurstCompile]
    partial struct BuildHeightsBillow : IBatchPQSHeightJob { }

    [StructInherit(typeof(BuildHeightsBase<IModule>))]
    partial struct BuildHeightsFallback : IBatchPQSHeightJob { }
}
