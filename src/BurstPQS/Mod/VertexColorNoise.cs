using BurstPQS.Noise;
using Unity.Burst;
using UnityEngine;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSMod_VertexColorNoise))]
public class VertexColorNoise(PQSMod_VertexColorNoise mod) : BatchPQSMod<PQSMod_VertexColorNoise>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        if (mod.noiseMap is LibNoise.Perlin perlin)
            jobSet.Add(new PerlinJob { noise = new(perlin), blend = mod.blend });
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            jobSet.Add(new RidgedMultifractalJob { noise = new(multi), blend = mod.blend });
        else if (mod.noiseMap is LibNoise.Billow billow)
            jobSet.Add(new BillowJob { noise = new(billow), blend = mod.blend });
        else
            jobSet.Add(new FallbackJob { noise = mod.noiseMap, blend = mod.blend });
    }

    [BurstCompile]
    struct PerlinJob : IBatchPQSVertexJob
    {
        public BurstPerlin noise;
        public float blend;

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
    struct RidgedMultifractalJob : IBatchPQSVertexJob
    {
        public BurstRidgedMultifractal noise;
        public float blend;

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
    struct BillowJob : IBatchPQSVertexJob
    {
        public BurstBillow noise;
        public float blend;

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

    struct FallbackJob : IBatchPQSVertexJob
    {
        public IModule noise;
        public float blend;

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
}
