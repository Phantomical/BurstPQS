using System;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VoronoiCraters2))]
public class VoronoiCraters2(PQSMod_VoronoiCraters2 mod) : BatchPQSMod<PQSMod_VoronoiCraters2>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                voronoi = new(mod.voronoi),
                jitterSimplex = new BurstSimplex(mod.jitterSimplex),
                craterCurve = new BurstAnimationCurve(mod.craterCurve),
                deformationSimplex = new BurstSimplex(mod.deformationSimplex),
                craterColorRamp = new BurstGradient(mod.craterColourRamp),
                jitter = mod.jitter,
                deformation = mod.deformation,
                debugColorMapping = mod.DebugColorMapping,
            }
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    unsafe struct BuildJob : IBatchPQSHeightJob, IBatchPQSVertexJob, IDisposable
    {
        public BurstVoronoi voronoi;
        public BurstSimplex jitterSimplex;
        public BurstAnimationCurve craterCurve;
        public BurstSimplex deformationSimplex;
        public BurstGradient craterColorRamp;
        public double jitter;
        public double deformation;
        public bool debugColorMapping;

        float* rs;

        public void BuildHeights(in BuildHeightsData data)
        {
            rs = (float*)
                UnsafeUtility.Malloc(
                    data.VertexCount * sizeof(float),
                    4,
                    Unity.Collections.Allocator.Temp
                );

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var nearest = voronoi.GetNearest(data.directionFromCenter[i]);
                var vd = nearest - data.directionFromCenter[i];
                var h = MathUtil.Clamp01(vd.magnitude * 1.7320508075688772);
                var s = jitterSimplex.noise(data.directionFromCenter[i]) * jitter;
                var r = craterCurve.Evaluate((float)(h + s));
                var d = deformationSimplex.noiseNormalized(nearest) * deformation;

                rs[i] = r;
                data.vertHeight[i] += d * r;
            }
        }

        public void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                float rf = rs[i];
                float rfN = (rf + 1f) * 0.5f;

                if (debugColorMapping)
                    data.vertColor[i] = Color.Lerp(Color.magenta, data.vertColor[i], rfN);
                else
                    data.vertColor[i] = Color.Lerp(
                        craterColorRamp.Evaluate(rf),
                        data.vertColor[i],
                        rfN
                    );
            }
        }

        public void Dispose()
        {
            if (rs != null)
            {
                UnsafeUtility.Free(rs, Unity.Collections.Allocator.Temp);
                rs = null;
            }

            voronoi.Dispose();
            jitterSimplex.Dispose();
            craterCurve.Dispose();
            deformationSimplex.Dispose();
            craterColorRamp.Dispose();
        }
    }
}
