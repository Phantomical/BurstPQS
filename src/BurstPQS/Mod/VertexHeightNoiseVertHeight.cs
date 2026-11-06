using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeight))]
public class VertexHeightNoiseVertHeight(PQSMod_VertexHeightNoiseVertHeight mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeight>(mod)
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
            new BuildHeightsJob
            {
                noise = noise,
                sphereRadiusMin = mod.sphere.radiusMin,
                sphereRadiusDelta = mod.sphere.radiusDelta,
                heightStart = mod.heightStart,
                heightEnd = mod.heightEnd,
                hDeltaR = mod.hDeltaR,
                deformity = mod.deformity,
            }
        );
    }

    public override void Dispose() => noise.Dispose();

    [BurstCompile]
    struct BuildHeightsJob : IBatchPQSHeightJob
    {
        public NoiseProgram noise;
        public double sphereRadiusMin;
        public double sphereRadiusDelta;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            using var values = noise.Evaluate(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = (data.vertHeight[i] - sphereRadiusMin) / sphereRadiusDelta;
                if (h < heightStart || h > heightEnd)
                    continue;
                h = (h - heightStart) * hDeltaR;
                double n = MathUtil.Clamp(values[i], -1.0, 1.0);

                data.vertHeight[i] += (n + 1.0) * 0.5 * deformity * h;
            }
        }
    }
}
