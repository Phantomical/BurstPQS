using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightNoiseHeightMap : PQSMod_VertexHeightNoiseHeightMap, IBatchPQSMod
{
    static float[] HeightMapData;

    public unsafe void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        int vc = data.VertexCount;
        if (HeightMapData is null || HeightMapData.Length != vc)
            HeightMapData = new float[vc];

        fixed (float* heightMapData = HeightMapData)
        {
            for (int i = 0; i < vc; ++i)
            {
                heightMapData[i] = heightMap
                    .GetPixelBilinear((float)data.sx[i], (float)data.sy[i])
                    .grayscale;
            }

            var hmap = new MemorySpan<float>(heightMapData, data.VertexCount);

            if (noiseMap is LibNoise.Perlin perlin)
                BuildVertexPerlin(
                    in data.burstData,
                    new(perlin),
                    hmap,
                    heightStart,
                    heightEnd,
                    hDeltaR,
                    deformity
                );
            else if (noiseMap is LibNoise.RidgedMultifractal multi)
                BuildVertexRidgedMultifractal(
                    in data.burstData,
                    new(multi),
                    hmap,
                    heightStart,
                    heightEnd,
                    hDeltaR,
                    deformity
                );
            else if (noiseMap is LibNoise.Billow billow)
                BuildVertexBillow(
                    in data.burstData,
                    new(billow),
                    hmap,
                    heightStart,
                    heightEnd,
                    hDeltaR,
                    deformity
                );
            else
                BuildVertex(
                    in data.burstData,
                    noiseMap,
                    hmap,
                    heightStart,
                    heightEnd,
                    hDeltaR,
                    deformity
                );
        }
    }

    public void OnQuadBuildVertex(in QuadBuildData data) { }

    static void BuildVertex<N>(
        in BurstQuadBuildData data,
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
        in BurstQuadBuildData data,
        in Perlin noise,
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
        in BurstQuadBuildData data,
        in RidgedMultifractal noise,
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
        in BurstQuadBuildData data,
        in Billow noise,
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
