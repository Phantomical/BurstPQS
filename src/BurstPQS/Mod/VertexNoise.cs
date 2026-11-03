using BurstPQS.Collections;
using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexNoise))]
public class VertexNoise(PQSMod_VertexNoise mod) : BatchPQSMod<PQSMod_VertexNoise>(mod)
{
    NoiseProgram noise;

    public override void OnSetup()
    {
        base.OnSetup();
        noise = CompileNoise(mod.terrainHeightMap);
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                noise = noise,
                sphereRadius = mod.sphere.radius,
                noiseDeformity = mod.noiseDeformity,
            }
        );
    }

    public override void Dispose() => noise.Dispose();

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public NoiseProgram noise;
        public double sphereRadius;
        public double noiseDeformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            using var coordArray = new NativeArray<Vector3d>(
                data.VertexCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            var coords = new MemorySpan<Vector3d>(coordArray);
            for (int i = 0; i < data.VertexCount; ++i)
                coords[i] = data.directionFromCenter[i] * sphereRadius;

            using var values = noise.Evaluate(coords, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += values[i] * noiseDeformity;
        }
    }
}
