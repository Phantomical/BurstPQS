using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorNoiseRGB))]
public class VertexColorNoiseRGB(PQSMod_VertexColorNoiseRGB mod)
    : BatchPQSMod<PQSMod_VertexColorNoiseRGB>(mod)
{
    NoiseProgram noise;

    public override void OnSetup()
    {
        base.OnSetup();
        noise = CompileNoise(mod.noiseMap);
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildVerticesJob
            {
                noise = noise,
                blend = new Blends
                {
                    r = mod.rBlend,
                    g = mod.gBlend,
                    b = mod.bBlend,
                    total = mod.blend,
                },
            }
        );
    }

    public override void Dispose() => noise.Dispose();

    struct Blends
    {
        public float r;
        public float g;
        public float b;
        public float total;
    }

    [BurstCompile]
    struct BuildVerticesJob : IBatchPQSVertexJob
    {
        public NoiseProgram noise;
        public Blends blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            using var values = noise.Evaluate(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var h = (float)((values[i] + 1.0) * 0.5);
                var c = new Color(h * blend.r, h * blend.g, h * blend.b, 1f);
                data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend.total);
            }
        }
    }
}
