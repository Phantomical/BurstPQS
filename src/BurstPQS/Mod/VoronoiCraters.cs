using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
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
    unsafe struct BuildJob : IBatchPQSHeightJob, IBatchPQSVertexJob, IDisposable
    {
        public BurstVoronoi voronoi;
        public BurstSimplex simplex;
        public BurstAnimationCurve jitterCurve;
        public BurstAnimationCurve craterCurve;
        public BurstGradient craterColorRamp;
        public double jitter;
        public double jitterHeight;
        public double deformation;
        public float rFactor;
        public float rOffset;
        public float colorOpacity;
        public bool debugColorMapping;

        float* rs;
        int vertexCount;

        public void BuildHeights(in BuildHeightsData data)
        {
            vertexCount = data.VertexCount;
            rs = (float*)
                UnsafeUtility.Malloc(
                    vertexCount * sizeof(float),
                    UnsafeUtility.AlignOf<float>(),
                    Unity.Collections.Allocator.Temp
                );

            for (int i = 0; i < data.VertexCount; ++i)
            {
                double vorH = voronoi.GetValue(data.directionFromCenter[i]);
                double spxH = simplex.noise(data.directionFromCenter[i]);
                double jtt = spxH * jitter * jitterCurve.Evaluate((float)vorH);
                double r = vorH + jtt;
                double h = craterCurve.Evaluate((float)r);

                rs[i] = (float)r;
                data.vertHeight[i] += (h + jitterHeight * jtt * h) * deformation;
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
            if (rs != null)
            {
                UnsafeUtility.Free(rs, Unity.Collections.Allocator.Temp);
                rs = null;
            }
            simplex.Dispose();
            jitterCurve.Dispose();
            craterCurve.Dispose();
            craterColorRamp.Dispose();
        }
    }
}
