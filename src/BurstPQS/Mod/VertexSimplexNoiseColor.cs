using System;
using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexNoiseColor))]
public class VertexSimplexNoiseColor(PQSMod_VertexSimplexNoiseColor mod)
    : BatchPQSMod<PQSMod_VertexSimplexNoiseColor>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildVerticesJob
            {
                simplex = new(mod.simplex),
                colorStart = mod.colorStart,
                colorEnd = mod.colorEnd,
                blend = mod.blend,
            }
        );
    }

    [BurstCompile]
    struct BuildVerticesJob : IBatchPQSVertexJob, IDisposable
    {
        public BurstSimplex simplex;
        public Color colorStart;
        public Color colorEnd;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            using var noise = simplex.GetValuesNormalized(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var n = (float)noise[i];
                var c = Color.Lerp(colorStart, colorEnd, n);

                data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
            }
        }

        public void Dispose()
        {
            simplex.Dispose();
        }
    }
}
