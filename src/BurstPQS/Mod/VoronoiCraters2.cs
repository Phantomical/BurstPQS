using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VoronoiCraters2))]
public class VoronoiCraters2(PQSMod_VoronoiCraters2 mod) : BatchPQSMod<PQSMod_VoronoiCraters2>(mod)
{
    BurstVoronoi voronoi;
    BurstSimplex jitterSimplex;
    BurstAnimationCurve craterCurve;
    BurstSimplex deformationSimplex;
    BurstGradient craterColorRamp;

    public override void OnSetup()
    {
        base.OnSetup();

        voronoi = new(mod.voronoi);
        jitterSimplex = new(mod.jitterSimplex);
        craterCurve = new(mod.craterCurve);
        deformationSimplex = new(mod.deformationSimplex);
        craterColorRamp = new(mod.craterColourRamp);
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                voronoi = voronoi,
                jitterSimplex = jitterSimplex,
                craterCurve = craterCurve,
                deformationSimplex = deformationSimplex,
                craterColorRamp = craterColorRamp,
                jitter = mod.jitter,
                deformation = mod.deformation,
                debugColorMapping = mod.DebugColorMapping,
            }
        );
    }

    public override void Dispose()
    {
        jitterSimplex.Dispose();
        craterCurve.Dispose();
        deformationSimplex.Dispose();
        craterColorRamp.Dispose();
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct BuildJob : IBatchPQSHeightJob, IBatchPQSVertexJob
    {
        public BurstVoronoi voronoi;
        public BurstSimplex jitterSimplex;
        public BurstAnimationCurve craterCurve;
        public BurstSimplex deformationSimplex;
        public BurstGradient craterColorRamp;
        public double jitter;
        public double deformation;
        public bool debugColorMapping;

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
    }
}
