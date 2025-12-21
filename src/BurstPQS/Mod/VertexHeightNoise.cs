using BurstPQS.Noise;
using Unity.Burst;
using Unity.Jobs;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoise))]
public class VertexHeightNoise(PQSMod_VertexHeightNoise mod)
    : BatchPQSMod<PQSMod_VertexHeightNoise>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var p = new BuildHeightsData { data = data.burst, deformity = mod.deformity };

        switch (mod.noiseMap)
        {
            case LibNoise.Perlin perlin:
                var pjob = new BuildHeightsPerlin { data = p, noise = new(perlin) };
                handle = pjob.Schedule(handle);
                break;

            case LibNoise.RidgedMultifractal multi:
                var mjob = new BuildHeightsRidgedMultifractal { data = p, noise = new(multi) };
                handle = mjob.Schedule(handle);
                break;

            case LibNoise.Billow billow:
                var bjob = new BuildHeightsBillow { data = p, noise = new(billow) };
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
        public double deformity;

        public void Execute<N>(in N noise)
            where N : IModule
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += noise.GetValue(data.directionFromCenter[i]) * deformity;
        }
    }

    [BurstCompile]
    struct BuildHeightsPerlin : IJob
    {
        public BuildHeightsData data;
        public BurstPerlin noise;

        public void Execute() => data.Execute(in noise);
    }

    [BurstCompile]
    struct BuildHeightsRidgedMultifractal : IJob
    {
        public BuildHeightsData data;
        public BurstRidgedMultifractal noise;

        public void Execute() => data.Execute(in noise);
    }

    [BurstCompile]
    struct BuildHeightsBillow : IJob
    {
        public BuildHeightsData data;
        public BurstBillow noise;

        public void Execute() => data.Execute(in noise);
    }
}
