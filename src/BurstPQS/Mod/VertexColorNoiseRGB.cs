using BurstPQS.Noise;
using Unity.Burst;
using UnityEngine;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSMod_VertexColorNoiseRGB))]
public class VertexColorNoiseRGB(PQSMod_VertexColorNoiseRGB mod)
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
            jobSet.Add(new PerlinJob { noise = new(perlin), blend = blend });
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            jobSet.Add(new RidgedMultifractalJob { noise = new(multi), blend = blend });
        else if (mod.noiseMap is LibNoise.Billow billow)
            jobSet.Add(new BillowJob { noise = new(billow), blend = blend });
        else
            jobSet.Add(new FallbackJob { noise = mod.noiseMap, blend = blend });
    }

    struct Blends
    {
        public float r;
        public float g;
        public float b;
        public float total;
    }

    [BurstCompile]
    struct PerlinJob : IBatchPQSVertexJob
    {
        public BurstPerlin noise;
        public Blends blend;

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
    struct RidgedMultifractalJob : IBatchPQSVertexJob
    {
        public BurstRidgedMultifractal noise;
        public Blends blend;

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
    struct BillowJob : IBatchPQSVertexJob
    {
        public BurstBillow noise;
        public Blends blend;

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

    struct FallbackJob : IBatchPQSVertexJob
    {
        public IModule noise;
        public Blends blend;

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
}
