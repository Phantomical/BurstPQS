using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexColorRGB))]
public class VertexSimplexColorRGB(PQSMod_VertexSimplexColorRGB mod)
    : BatchPQSMod<PQSMod_VertexSimplexColorRGB>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        using var simplex = new BurstSimplex(mod.simplex);

        jobSet.Add(
            new BuildJob
            {
                simplex = simplex,
                rBlend = mod.rBlend,
                gBlend = mod.gBlend,
                bBlend = mod.bBlend,
                blend = mod.blend,
            }
        );
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob, IDisposable
    {
        public BurstSimplex simplex;
        public float rBlend;
        public float gBlend;
        public float bBlend;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            float3 cblend = new(rBlend, gBlend, bBlend);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                float n = (float)simplex.noise(data.directionFromCenter[i]);
                float4 c = new(n * cblend, Color.white.a);

                data.vertColor[i] = Color.Lerp(data.vertColor[i], BurstUtil.ConvertColor(c), blend);
            }
        }

        public void Dispose()
        {
            simplex.Dispose();
        }
    }
}
