using BurstPQS.Noise;
using LibNoise.Modifiers;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexNoise))]
public class VertexNoise(PQSMod_VertexNoise mod) : BatchPQSMod<PQSMod_VertexNoise>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        var control = (LibNoise.Perlin)mod.terrainHeightMap.ControlModule;
        var input = (ScaleBiasOutput)mod.terrainHeightMap.SourceModule1;
        var billow = (LibNoise.Billow)input.SourceModule;
        var ridged = (LibNoise.RidgedMultifractal)mod.terrainHeightMap.SourceModule2;

        var noise = new Select<BurstPerlin, ScaleBiasOutput<BurstBillow>, BurstRidgedMultifractal>(
            mod.terrainHeightMap,
            new(control),
            new(input, new(billow)),
            new(ridged)
        );

        jobSet.Add(new BuildJob
        {
            terrainHeightMap = noise,
            sphereRadius = mod.sphere.radius,
            noiseDeformity = mod.noiseDeformity
        });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public Select<BurstPerlin, ScaleBiasOutput<BurstBillow>, BurstRidgedMultifractal> terrainHeightMap;
        public double sphereRadius;
        public double noiseDeformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] +=
                    terrainHeightMap.GetValue(data.directionFromCenter[i] * sphereRadius)
                    * noiseDeformity;
        }
    }
}
