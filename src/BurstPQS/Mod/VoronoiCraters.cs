using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VoronoiCraters))]
public class VoronoiCraters(PQSMod_VoronoiCraters mod) : BatchPQSMod<PQSMod_VoronoiCraters>(mod)
{
    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data)
    {
        return new State(mod);
    }

    class State(PQSMod_VoronoiCraters mod) : BatchPQSModState<PQSMod_VoronoiCraters>(mod)
    {
        NativeArray<float> rs;

        public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
        {
            rs = new NativeArray<float>(data.VertexCount, Allocator.TempJob);

            var job = new BuildHeightsJob
            {
                data = data.burst,
                voronoi = new(mod.voronoi),
                simplex = new(mod.simplex),
                jitterCurve = new(mod.jitterCurve),
                craterCurve = new(mod.craterCurve),
                rs = rs,
                jitter = mod.jitter,
                jitterHeight = mod.jitterHeight,
                deformation = mod.deformation,
            };

            handle = job.Schedule(handle);
            job.simplex.Dispose(handle);
            job.jitterCurve.Dispose(handle);
            job.craterCurve.Dispose(handle);

            return handle;
        }

        public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
        {
            var job = new BuildVerticesJob
            {
                data = data.burst,
                craterColorRamp = new(mod.craterColourRamp),
                rs = rs,
                rFactor = mod.rFactor,
                rOffset = mod.rOffset,
                colorOpacity = mod.colorOpacity,
                debugColorMapping = mod.DebugColorMapping,
            };

            handle = job.Schedule(handle);
            job.craterColorRamp.Dispose(handle);
            rs.Dispose(handle);

            return handle;
        }

        public override void Dispose()
        {
            rs.Dispose();
        }
    }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstVoronoi voronoi;
        public BurstSimplex simplex;
        public BurstAnimationCurve jitterCurve;
        public BurstAnimationCurve craterCurve;
        public NativeArray<float> rs;
        public double jitter;
        public double jitterHeight;
        public double deformation;

        public void Execute()
        {
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
    }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstGradient craterColorRamp;
        public NativeArray<float> rs;
        public float rFactor;
        public float rOffset;
        public float colorOpacity;
        public bool debugColorMapping;

        public void Execute()
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
    }
}
