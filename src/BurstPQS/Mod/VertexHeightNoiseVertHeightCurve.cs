using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseVertHeightCurve))]
public class VertexHeightNoiseVertHeightCurve(PQSMod_VertexHeightNoiseVertHeightCurve mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseVertHeightCurve>(mod)
{
    NoiseProgram noise;
    BurstAnimationCurve curve;

    public override void OnSetup()
    {
        base.OnSetup();
        noise = CompileNoise(mod.noiseMap);
        curve = new BurstAnimationCurve(mod.curve);
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildHeightsJob
            {
                noise = noise,
                curve = curve,
                sphereRadiusMin = mod.sphere.radiusMin,
                heightStart = mod.heightStart,
                heightEnd = mod.heightEnd,
                deformity = mod.deformity,
            }
        );
    }

    public override void Dispose()
    {
        noise.Dispose();
        curve.Dispose();
    }

    [BurstCompile]
    struct BuildHeightsJob : IBatchPQSHeightJob
    {
        public NoiseProgram noise;
        public BurstAnimationCurve curve;
        public double sphereRadiusMin;
        public float heightStart;
        public float heightEnd;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            using var values = noise.Evaluate(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = data.vertHeight[i] - sphereRadiusMin;
                float t;
                if (h <= heightStart)
                    t = 0f;
                else if (h >= heightEnd)
                    t = 1f;
                else
                    t = (float)((h - heightStart) / (heightEnd - heightStart));

                double n = MathUtil.Clamp(values[i], -1.0, 1.0);
                data.vertHeight[i] += (n + 1.0) * 0.5 * deformity * curve.Evaluate(t);
            }
        }
    }
}
