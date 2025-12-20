using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightNoiseHeightMap : BatchPQSModV1<PQSMod_VertexHeightNoiseHeightMap>
{
    static float[] HeightMapData;

    public VertexHeightNoiseHeightMap(PQSMod_VertexHeightNoiseHeightMap mod)
        : base(mod) { }

    public override unsafe void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        int vc = data.VertexCount;
        if (HeightMapData is null || HeightMapData.Length != vc)
            HeightMapData = new float[vc];

        fixed (float* heightMapData = HeightMapData)
        {
            for (int i = 0; i < vc; ++i)
            {
                heightMapData[i] = mod
                    .heightMap.GetPixelBilinear((float)data.sx[i], (float)data.sy[i])
                    .grayscale;
            }

            var hmap = new MemorySpan<float>(heightMapData, data.VertexCount);

            if (mod.noiseMap is LibNoise.Perlin perlin)
                BuildVertexPerlin(
                    in data.burstData,
                    new(perlin),
                    hmap,
                    mod.heightStart,
                    mod.heightEnd,
                    mod.hDeltaR,
                    mod.deformity
                );
            else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
                BuildVertexRidgedMultifractal(
                    in data.burstData,
                    new(multi),
                    hmap,
                    mod.heightStart,
                    mod.heightEnd,
                    mod.hDeltaR,
                    mod.deformity
                );
            else if (mod.noiseMap is LibNoise.Billow billow)
                BuildVertexBillow(
                    in data.burstData,
                    new(billow),
                    hmap,
                    mod.heightStart,
                    mod.heightEnd,
                    mod.hDeltaR,
                    mod.deformity
                );
            else
                BuildVertex(
                    in data.burstData,
                    mod.noiseMap,
                    hmap,
                    mod.heightStart,
                    mod.heightEnd,
                    mod.hDeltaR,
                    mod.deformity
                );
        }
    }

    static void BuildVertex<N>(
        in BurstQuadBuildDataV1 data,
        in N noise,
        in MemorySpan<float> heightMapData,
        float heightStart,
        float heightEnd,
        double hDeltaR,
        float deformity
    )
        where N : IModule
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            double h = heightMapData[i];
            if (h < heightStart || h > heightEnd)
                continue;

            h = (h - heightStart) * hDeltaR;
            double n = MathUtil.Clamp(noise.GetValue(data.directionFromCenter[i]), -1d, 1d);

            data.vertHeight[i] += (n + 1.0) * 0.5 * deformity * h;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexPerlin(
        in BurstQuadBuildDataV1 data,
        in BurstPerlin noise,
        in MemorySpan<float> heightMapData,
        float heightStart,
        float heightEnd,
        double hDeltaR,
        float deformity
    ) =>
        BuildVertex(
            in data,
            in noise,
            in heightMapData,
            heightStart,
            heightEnd,
            hDeltaR,
            deformity
        );

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        in BurstQuadBuildDataV1 data,
        in BurstRidgedMultifractal noise,
        in MemorySpan<float> heightMapData,
        float heightStart,
        float heightEnd,
        double hDeltaR,
        float deformity
    ) =>
        BuildVertex(
            in data,
            in noise,
            in heightMapData,
            heightStart,
            heightEnd,
            hDeltaR,
            deformity
        );

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(
        in BurstQuadBuildDataV1 data,
        in BurstBillow noise,
        in MemorySpan<float> heightMapData,
        float heightStart,
        float heightEnd,
        double hDeltaR,
        float deformity
    ) =>
        BuildVertex(
            in data,
            in noise,
            in heightMapData,
            heightStart,
            heightEnd,
            hDeltaR,
            deformity
        );
}
