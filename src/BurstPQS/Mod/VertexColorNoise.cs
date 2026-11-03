using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorNoise))]
public class VertexColorNoise(PQSMod_VertexColorNoise mod)
    : BatchPQSMod<PQSMod_VertexColorNoise>(mod)
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
        jobSet.Add(new BuildVerticesJob { noise = noise, blend = mod.blend });
    }

    public override void Dispose() => noise.Dispose();

    [BurstCompile]
    struct BuildVerticesJob : IBatchPQSVertexJob
    {
        public NoiseProgram noise;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            using var values = noise.Evaluate(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var h = (float)((values[i] + 1.0) * 0.5);
                var c = new Color(h, h, h, 1f);
                data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
            }
        }
    }
}
