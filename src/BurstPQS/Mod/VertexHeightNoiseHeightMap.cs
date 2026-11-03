using BurstPQS.Map;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightNoiseHeightMap))]
public class VertexHeightNoiseHeightMap(PQSMod_VertexHeightNoiseHeightMap mod)
    : BatchPQSMod<PQSMod_VertexHeightNoiseHeightMap>(mod)
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
                heightMap = TextureMapSO.Create(mod.heightMap),
                noise = noise,
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
        public BurstMapSO heightMap;
        public NoiseProgram noise;
        public float heightStart;
        public float heightEnd;
        public double hDeltaR;
        public float deformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            using var values = noise.Evaluate(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var c = heightMap.GetPixelColor((float)data.sx[i], (float)data.sy[i]);
                double h = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                if (h < heightStart || h > heightEnd)
                    continue;

                h = (h - heightStart) * hDeltaR;
                double n = MathUtil.Clamp(values[i], -1d, 1d);

                data.vertHeight[i] += (n + 1.0) * 0.5 * deformity * h;
            }
        }
    }
}
