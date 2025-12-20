using BurstPQS.Noise;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexNoiseColor))]
public class VertexSimplexNoiseColor(PQSMod_VertexSimplexNoiseColor mod)
    : BatchPQSMod<PQSMod_VertexSimplexNoiseColor>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildVerticesJob
        {
            data = data.burst,
            simplex = new(mod.simplex),
            colorStart = mod.colorStart,
            colorEnd = mod.colorEnd,
            blend = mod.blend,
        };
        handle = job.Schedule(handle);
        job.simplex.Dispose(handle);

        return handle;
    }

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstSimplex simplex;
        public Color colorStart;
        public Color colorEnd;
        public float blend;

        public void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var dir = data.directionFromCenter[i];
                var n = (float)((simplex.noise(dir) + 1.0) * 0.5);
                var c = Color.Lerp(colorStart, colorEnd, n);

                data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
            }
        }
    }
}
