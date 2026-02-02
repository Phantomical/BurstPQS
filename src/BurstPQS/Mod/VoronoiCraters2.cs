using System;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Mod;

// [BurstCompile]
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
    struct BuildJob(PQSMod_VoronoiCraters2 mod)
        : IBatchPQSHeightJob,
            IBatchPQSVertexJob,
            IDisposable
    {
        public BurstVoronoi voronoi = new(mod.voronoi);
        public BurstSimplex jitterSimplex = new(mod.jitterSimplex);
        public BurstAnimationCurve craterCurve = new(mod.craterCurve);
        public BurstSimplex deformationSimplex = new(mod.deformationSimplex);
        public BurstGradient craterColorRamp = new(mod.craterColourRamp);
        public double jitter = mod.jitter;
        public double deformation = mod.deformation;
        public bool debugColorMapping = mod.DebugColorMapping;

        NativeArray<float> rs;

        public void BuildHeights(in BuildHeightsData data)
        {
            rs = new(data.VertexCount, Allocator.Temp);

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
            jitterSimplex.Dispose();
            craterCurve.Dispose();
            deformationSimplex.Dispose();
            craterColorRamp.Dispose();
        }
    }
}
