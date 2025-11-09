using BurstPQS.Noise;
using BurstPQS.Util;
using LibNoise.Modifiers;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexNoise : BatchPQSMod<PQSMod_VertexNoise>
{
    public VertexNoise(PQSMod_VertexNoise mod)
        : base(mod) { }

    public override void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        var control = (LibNoise.Perlin)mod.terrainHeightMap.ControlModule;
        var input = (ScaleBiasOutput)mod.terrainHeightMap.SourceModule1;
        var billow = (LibNoise.Billow)input.SourceModule;
        var ridged = (LibNoise.RidgedMultifractal)mod.terrainHeightMap.SourceModule2;

        var noise = new Select<Perlin, ScaleBiasOutput<Billow>, RidgedMultifractal>(
            mod.terrainHeightMap,
            new(control),
            new(input, new(billow)),
            new(ridged)
        );

        BuildHeight(in data.burstData, in noise, mod.sphere.radius, mod.noiseDeformity);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeight(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in Select<Perlin, ScaleBiasOutput<Billow>, RidgedMultifractal> terrainHeightMap,
        double sphereRadius,
        double noiseDeformity
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
            data.vertHeight[i] +=
                terrainHeightMap.GetValue(data.directionFromCenter[i] * sphereRadius)
                * noiseDeformity;
    }
}
