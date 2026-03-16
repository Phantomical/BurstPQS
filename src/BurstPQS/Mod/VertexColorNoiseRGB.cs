using BurstPQS.CompilerServices;
using BurstPQS.Noise;
using Unity.Burst;
using UnityEngine;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSMod_VertexColorNoiseRGB))]
public partial class VertexColorNoiseRGB(PQSMod_VertexColorNoiseRGB mod)
    : BatchPQSMod<PQSMod_VertexColorNoiseRGB>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        var blend = new Blends
        {
            r = mod.rBlend,
            g = mod.gBlend,
            b = mod.bBlend,
            total = mod.blend,
        };

        if (mod.noiseMap is LibNoise.Perlin perlin)
            jobSet.Add(new PerlinJob(new(perlin), blend));
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            jobSet.Add(new RidgedMultifractalJob(new(multi), blend));
        else if (mod.noiseMap is LibNoise.Billow billow)
            jobSet.Add(new BillowJob(new(billow), blend));
        else
            jobSet.Add(new FallbackJob(mod.noiseMap, blend));
    }

    struct Blends
    {
        public float r;
        public float g;
        public float b;
        public float total;
    }

    struct BuildVerticesBase<N>(N noise, Blends blend)
        where N : IModule
    {
        public N noise = noise;
        public Blends blend = blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var h = (float)((noise.GetValue(data.directionFromCenter[i]) + 1.0) * 0.5);
                var c = new Color(h * blend.r, h * blend.g, h * blend.b, 1f);
                data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend.total);
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
