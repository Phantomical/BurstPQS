using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoise))]
public class VertexHeightNoise(PQSMod_VertexHeightNoise mod)
    : BatchPQSMod<PQSMod_VertexHeightNoise>(mod)
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
        jobSet.Add(new BuildHeightsJob { noise = noise, deformity = mod.deformity });
    }

    public override void Dispose() => noise.Dispose();

    [BurstCompile]
    struct BuildHeightsJob : IBatchPQSHeightJob
    {
        public NoiseProgram noise;
        public double deformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            using var values = noise.Evaluate(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += values[i] * deformity;
        }
    }
}
