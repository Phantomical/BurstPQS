using BurstPQS.CompilerServices;
using BurstPQS.Noise;
using Unity.Burst;
using UnityEngine;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorNoise))]
public partial class VertexColorNoise(PQSMod_VertexColorNoise mod)
    : BatchPQSMod<PQSMod_VertexColorNoise>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        if (mod.noiseMap is LibNoise.Perlin perlin)
            jobSet.Add(new PerlinJob(new(perlin), mod.blend));
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            jobSet.Add(new RidgedMultifractalJob(new(multi), mod.blend));
        else if (mod.noiseMap is LibNoise.Billow billow)
            jobSet.Add(new BillowJob(new(billow), mod.blend));
        else
            jobSet.Add(new FallbackJob(mod.noiseMap, mod.blend));
    }

    struct BuildVerticesBase<N>(N noise, float blend)
        where N : IModule
    {
        public N noise = noise;
        public float blend = blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var h = (float)((noise.GetValue(data.directionFromCenter[i]) + 1.0) * 0.5);
                var c = new Color(h, h, h, 1f);
                data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
            }
        }
    }

    [BurstCompile]
    [StructInherit(typeof(BuildVerticesBase<BurstPerlin>))]
    partial struct PerlinJob : IBatchPQSVertexJob { }

    [BurstCompile]
    [StructInherit(typeof(BuildVerticesBase<BurstRidgedMultifractal>))]
    partial struct RidgedMultifractalJob : IBatchPQSVertexJob { }

    [BurstCompile]
    [StructInherit(typeof(BuildVerticesBase<BurstBillow>))]
    partial struct BillowJob : IBatchPQSVertexJob { }

    [StructInherit(typeof(BuildVerticesBase<IModule>))]
    partial struct FallbackJob : IBatchPQSVertexJob { }
}
