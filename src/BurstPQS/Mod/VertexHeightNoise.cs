using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using IModule = LibNoise.IModule;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightNoise : BatchPQSModV1<PQSMod_VertexHeightNoise>
{
    public VertexHeightNoise(PQSMod_VertexHeightNoise mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        if (mod.noiseMap is LibNoise.Perlin perlin)
            BuildVertexPerlin(in data.burstData, new(perlin), mod.deformity);
        else if (mod.noiseMap is LibNoise.RidgedMultifractal multi)
            BuildVertexRidgedMultifractal(in data.burstData, new(multi), mod.deformity);
        else if (mod.noiseMap is LibNoise.Billow billow)
            BuildVertexBillow(in data.burstData, new(billow), mod.deformity);
        else
            BuildVertex(in data.burstData, mod.noiseMap, mod.deformity);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexPerlin(
        in BurstQuadBuildDataV1 data,
        in BurstPerlin noise,
        float deformity
    ) => BuildVertex(in data, in noise, deformity);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexRidgedMultifractal(
        in BurstQuadBuildDataV1 data,
        in BurstRidgedMultifractal noise,
        float blend
    ) => BuildVertex(in data, in noise, blend);

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertexBillow(
        in BurstQuadBuildDataV1 data,
        in BurstBillow noise,
        float deformity
    ) => BuildVertex(in data, in noise, deformity);

    static void BuildVertex<N>(in BurstQuadBuildDataV1 data, in N noise, float deformity)
        where N : IModule
    {
        for (int i = 0; i < data.VertexCount; ++i)
            data.vertHeight[i] += noise.GetValue(data.directionFromCenter[i]) * deformity;
    }
}
