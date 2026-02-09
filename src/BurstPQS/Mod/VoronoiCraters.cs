using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VoronoiCraters))]
public class VoronoiCraters(PQSMod_VoronoiCraters mod) : BatchPQSMod<PQSMod_VoronoiCraters>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                voronoi = new(mod.voronoi),
                simplex = new(mod.simplex),
                jitterCurve = new(mod.jitterCurve),
                craterCurve = new(mod.craterCurve),
                craterColorRamp = new(mod.craterColourRamp),
                jitter = mod.jitter,
                jitterHeight = mod.jitterHeight,
                deformation = mod.deformation,
                rFactor = mod.rFactor,
                rOffset = mod.rOffset,
                colorOpacity = mod.colorOpacity,
                debugColorMapping = mod.DebugColorMapping,
            }
        );
    }

    [BurstCompile]
    struct BuildJob(PQSMod_VoronoiCraters mod) : IBatchPQSHeightJob, IBatchPQSVertexJob, IDisposable
    {
        public BurstVoronoi voronoi = new(mod.voronoi);
        public BurstSimplex simplex = new(mod.simplex);
        public BurstAnimationCurve jitterCurve = new(mod.jitterCurve);
        public BurstAnimationCurve craterCurve = new(mod.craterCurve);
        public BurstGradient craterColorRamp = new(mod.craterColourRamp);
        public float jitter = mod.jitter;
        public float jitterHeight = mod.jitterHeight;
        public double deformation = mod.deformation;
        public float rFactor = mod.rFactor;
        public float rOffset = mod.rOffset;
        public float colorOpacity = mod.colorOpacity;
        public bool debugColorMapping = mod.DebugColorMapping;

        NativeArray<float> rs;

        public void BuildHeights(in BuildHeightsData data)
        {
            rs = new(data.VertexCount, Allocator.Temp);

            for (int i = 0; i < data.VertexCount; ++i)
            {
                float vorH = (float)voronoi.GetValue(data.directionFromCenter[i]);
                float spxH = (float)simplex.noise(data.directionFromCenter[i]);
                float jtt = spxH * jitter * jitterCurve.Evaluate(vorH);
                float r = vorH + jtt;
                float h = craterCurve.Evaluate(r);

                rs[i] = r;
                data.vertHeight[i] += ((double)h + (double)(jitterHeight * jtt * h)) * deformation;
            }
        }

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                float r = rs[i] * rFactor + rOffset;
                Color c;

                if (debugColorMapping)
                    c = Color.Lerp(Color.magenta, data.vertColor[i], r);
                else
                    c = Color.Lerp(
                        data.vertColor[i],
                        craterColorRamp.Evaluate(r),
                        (1f - r) * colorOpacity
                    );

                data.vertColor[i] = c;
            }
        }

        public void Dispose()
        {
            simplex.Dispose();
            jitterCurve.Dispose();
            craterCurve.Dispose();
            craterColorRamp.Dispose();
        }
    }
}
