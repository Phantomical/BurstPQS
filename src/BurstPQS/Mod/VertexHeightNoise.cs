using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile(FloatMode = FloatMode.Fast)]
public class VertexHeightNoise : PQSMod_VertexHeightNoise, IBatchPQSMod
{
    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        if (noiseMap is LibNoise.Perlin perlin)
            BuildVertexPerlin(in data.burstData, new(perlin), deformity);
        else if (noiseMap is LibNoise.RidgedMultifractal multi)
            BuildVertexRidgedMultifractal(in data.burstData, new(multi), deformity);
        else if (noiseMap is LibNoise.Billow billow)
            BuildVertexBillow(in data.burstData, new(billow), deformity);
        else
            BuildVertex(in data.burstData, noiseMap, deformity);
    }

    public void OnQuadBuildVertex(in QuadBuildData data) { }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVertexPerlin(in BurstQuadBuildData data, in Perlin noise, float deformity) =>
        BuildVertex(in data, in noise, deformity);

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        in BurstQuadBuildData data,
        in RidgedMultifractal noise,
        float blend
    ) => BuildVertex(in data, in noise, blend);

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(in BurstQuadBuildData data, in Billow noise, float deformity) =>
        BuildVertex(in data, in noise, deformity);

    static void BuildVertex<N>(in BurstQuadBuildData data, in N noise, float deformity)
        where N : IModule
    {
        for (int i = 0; i < data.VertexCount; ++i)
            data.vertHeight[i] += noise.GetValue(data.directionFromCenter[i]) * deformity;
    }
}
