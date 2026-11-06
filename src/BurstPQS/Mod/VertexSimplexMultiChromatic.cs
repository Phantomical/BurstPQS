using System;
using BurstPQS.Noise;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexSimplexMultiChromatic))]
public class VertexSimplexMultiChromatic(PQSMod_VertexSimplexMultiChromatic mod)
    : BatchPQSMod<PQSMod_VertexSimplexMultiChromatic>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        using var rSimplex = new BurstSimplex(mod.redSimplex);
        using var gSimplex = new BurstSimplex(mod.greenSimplex);
        using var bSimplex = new BurstSimplex(mod.blueSimplex);
        using var aSimplex = new BurstSimplex(mod.alphaSimplex);

        jobSet.Add(
            new BuildJob
            {
                rSimplex = rSimplex,
                gSimplex = gSimplex,
                bSimplex = bSimplex,
                aSimplex = aSimplex,
                blend = mod.blend,
            }
        );
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob, IDisposable
    {
        public BurstSimplex rSimplex;
        public BurstSimplex gSimplex;
        public BurstSimplex bSimplex;
        public BurstSimplex aSimplex;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            using var r = rSimplex.GetValuesNormalized(data.directionFromCenter, Allocator.Temp);
            using var g = gSimplex.GetValuesNormalized(data.directionFromCenter, Allocator.Temp);
            using var b = bSimplex.GetValuesNormalized(data.directionFromCenter, Allocator.Temp);
            using var a = aSimplex.GetValuesNormalized(data.directionFromCenter, Allocator.Temp);
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var c = new Color((float)r[i], (float)b[i], (float)g[i], (float)a[i]);

                data.vertColor[i] = Color.Lerp(data.vertColor[i], c, blend);
            }
        }

        public void Dispose()
        {
            rSimplex.Dispose();
            gSimplex.Dispose();
            bSimplex.Dispose();
            aSimplex.Dispose();
        }
    }
}
