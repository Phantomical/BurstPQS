using System;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSLandControl))]
public class LandControl(PQSLandControl mod) : BatchPQSMod<PQSLandControl>(mod)
{
    public readonly struct BurstLerpRange(PQSLandControl.LerpRange range)
    {
        public readonly double startStart = range.startStart;
        public readonly double startEnd = range.startEnd;
        public readonly double endStart = range.endStart;
        public readonly double endEnd = range.endEnd;
        public readonly double startDelta = range.startDelta;
        public readonly double endDelta = range.endDelta;

        public double Lerp(double point)
        {
            if (point <= startStart)
                return 0.0;
            if (point < startEnd)
                return (point - startStart) * startDelta;
            if (point <= endStart)
                return 1.0;
            if (point < endEnd)
                return 1.0 - (point - endStart) * endDelta;
            return 0.0;
        }
    }

    public struct BurstLandClass(PQSLandControl.LandClass lc) : IDisposable
    {
        public BurstLerpRange altitudeRange = new(lc.altitudeRange);
        public BurstLerpRange latitudeRange = new(lc.latitudeRange);
        public bool latitudeDouble = lc.latitudeDouble;
        public BurstLerpRange latitudeDoubleRange = new(lc.latitudeDoubleRange);
        public BurstLerpRange longitudeRange = new(lc.longitudeRange);
        public BurstSimplex coverageSimplex = new(lc.coverageSimplex);
        public BurstSimplex noiseSimplex = new(lc.noiseSimplex);
        public double minimumRealHeight = lc.minimumRealHeight;
        public double alterRealHeight = lc.alterRealHeight;
        public double coverageBlend = lc.coverageBlend;

        public Color color = lc.color;
        public Color noiseColor = lc.noiseColor;
        public float noiseBlend = lc.noiseBlend;
        public float alterApparentHeight = lc.alterApparentHeight;

        public readonly void Dispose()
        {
            coverageSimplex.Dispose();
            noiseSimplex.Dispose();
        }
    }

    NativeArray<BurstLandClass> burstLandClasses;

    public override void OnSetup()
    {
        base.OnSetup();

        burstLandClasses = new NativeArray<BurstLandClass>(
            mod.landClasses.Length,
            Allocator.Persistent
        );
        for (int i = 0; i < mod.landClasses.Length; ++i)
            burstLandClasses[i] = new(mod.landClasses[i]);
    }

    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data)
    {
        mod.OnQuadPreBuild(data.buildQuad);

        return new State(this, data);
    }

    public override void Dispose()
    {
        base.Dispose();

        foreach (var blc in burstLandClasses)
            blc.Dispose();
        burstLandClasses.Dispose();
    }

    class State : BatchPQSModState
    {
        public PQSLandControl mod;

        public NativeArray<ulong> lcActive;
        public NativeArray<double> lcDeltas;
        public NativeArray<double> vHeights;

        public NativeArray<BurstLandClass> landClasses;

        public State(LandControl batchMod, QuadBuildData data)
        {
            mod = batchMod.Mod;
            landClasses = batchMod.burstLandClasses;

            int lcActiveCount = data.VertexCount * landClasses.Length;

            // These will all be initialized by the BuildHeights job
            lcActive = new NativeArray<ulong>(
                (lcActiveCount + 63) / 63,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );
            lcDeltas = new NativeArray<double>(
                lcActiveCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );
            vHeights = new NativeArray<double>(
                data.VertexCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );
        }

        public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
        {
            var job = new BuildHeightsJob
            {
                data = data.burst,
                landClasses = landClasses,
                lcActive = lcActive,
                lcDeltas = lcDeltas,
                vHeights = vHeights,

                heightMap = mod.useHeightMap ? new(mod.heightMap) : null,
                altitudeSimplex = new(mod.altitudeSimplex),
                latitudeSimplex = new(mod.latitudeSimplex),
                longitudeSimplex = new(mod.longitudeSimplex),

                altitudeBlend = mod.altitudeBlend,
                latitudeBlend = mod.latitudeBlend,
                longitudeBlend = mod.longitudeBlend,
                vHeightMax = mod.vHeightMax,
            };

            handle = job.Schedule(handle);
            job.heightMap?.Dispose(handle);
            job.altitudeSimplex.Dispose(handle);
            job.latitudeSimplex.Dispose(handle);
            job.longitudeSimplex.Dispose(handle);

            return handle;
        }

        public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
        {
            if (!mod.createColors)
                return handle;

            var job = new BuildVerticesJob
            {
                data = data.burst,
                landClasses = landClasses,
                lcActive = lcActive,
                lcDeltas = lcDeltas,
                vHeights = vHeights,
            };

            handle = job.Schedule(handle);
            vHeights.Dispose(handle);

            return handle;
        }

        public override JobHandle OnQuadBuilt(QuadBuildData data)
        {
            if (!mod.scatterActive)
                return default;

            var lcActive = new BitSpan(new MemorySpan<ulong>(this.lcActive));

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var baseIndex = i * landClasses.Length;

                for (int itr = 0; itr < landClasses.Length; ++itr)
                {
                    if (!lcActive[baseIndex + itr])
                        continue;
                    double delta = lcDeltas[baseIndex + itr];
                    if (delta <= 0.0)
                        continue;

                    var lc = mod.landClasses[itr];
                    if (!data.allowScatter[i] || delta <= 0.05)
                        continue;

                    foreach (var landClassScatterAmount in lc.scatter)
                    {
                        if (data.buildQuad.subdivision >= mod.scatterMinSubdiv)
                        {
                            mod.lcScatterList[landClassScatterAmount.scatterIndex] +=
                                landClassScatterAmount.density
                                * delta
                                * PQS.cacheVertCountReciprocal
                                * PQS.Global_ScatterFactor;
                            mod.scatterInstCount++;
                        }
                    }
                }
            }

            return default;
        }

        public override void Dispose()
        {
            lcActive.Dispose(default);
            lcDeltas.Dispose(default);
            vHeights.Dispose(default);
        }
    }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;

        [ReadOnly]
        public NativeArray<BurstLandClass> landClasses;

        public NativeArray<ulong> lcActive;
        public NativeArray<double> lcDeltas;
        public NativeArray<double> vHeights;

        [ReadOnly]
        public BurstMapSO? heightMap;
        public BurstSimplex altitudeSimplex;
        public BurstSimplex latitudeSimplex;
        public BurstSimplex longitudeSimplex;
        public double altitudeBlend;
        public double latitudeBlend;
        public double longitudeBlend;
        public double vHeightMax;

        public void Execute()
        {
            var lcActive = new BitSpan(new MemorySpan<ulong>(this.lcActive));

            lcActive.Clear();
            lcDeltas.Clear();
            vHeights.Clear();

            double sphereRadius = data.sphere.radius;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                double totalDelta = 0.0;
                double vHeight;

                data.vertColor[i] = Color.black;
                if (this.heightMap is BurstMapSO heightMap)
                    vHeight = heightMap.GetPixelFloat(data.u[i], data.v[i]);
                else
                    vHeight = (data.vertHeight[i] - sphereRadius) / vHeightMax;
                vHeight += altitudeBlend * altitudeSimplex.noise(data.directionFromCenter[i]);
                vHeight = Math.Min(vHeight, 1.0);

                double vLat =
                    data.sy[i] + latitudeBlend * latitudeSimplex.noise(data.directionFromCenter[i]);
                double vLon =
                    data.sx[i]
                    + longitudeBlend * longitudeSimplex.noise(data.directionFromCenter[i]);

                vLat = MathUtil.Clamp01(vLat);
                vLon = MathUtil.Clamp01(vLon);
                vHeights[i] = vHeight;

                int baseIndex = landClasses.Length * i;
                for (int itr = 0; itr < landClasses.Length; ++itr)
                {
                    var lc = landClasses[itr];
                    double altDelta = lc.altitudeRange.Lerp(vHeight);
                    double latDelta = lc.latitudeRange.Lerp(vLat);
                    double lonDelta = lc.longitudeRange.Lerp(vLon);

                    if (lc.latitudeDouble)
                        latDelta = Math.Max(lc.latitudeDoubleRange.Lerp(vLat), latDelta);

                    double delta = altDelta * latDelta * lonDelta;
                    delta = MathUtil.Lerp(
                        delta,
                        delta * lc.coverageSimplex.noiseNormalized(data.directionFromCenter[i]),
                        lc.coverageBlend
                    );

                    if (delta != 0.0)
                    {
                        lcDeltas[baseIndex + itr] = delta;
                        lcActive[baseIndex + itr] = true;
                        totalDelta += delta;
                    }
                }

                for (int itr = 0; itr < landClasses.Length; ++itr)
                {
                    var lc = landClasses[itr];
                    lcDeltas[baseIndex + itr] /= totalDelta;
                    double delta = lcDeltas[baseIndex + itr];

                    if (delta > 0.0)
                    {
                        if (
                            lc.minimumRealHeight != 0.0
                            && data.vertHeight[i] - sphereRadius < lc.minimumRealHeight
                        )
                            data.vertHeight[i] = sphereRadius + delta * lc.minimumRealHeight;

                        data.vertHeight[i] += delta * lc.alterRealHeight;
                    }
                }
            }
        }
    }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;

        [ReadOnly]
        public NativeArray<BurstLandClass> landClasses;

        [ReadOnly]
        public NativeArray<ulong> lcActive;

        [ReadOnly]
        public NativeArray<double> lcDeltas;

        [ReadOnly]
        public NativeArray<double> vHeights;

        public void Execute()
        {
            var lcActive = new BitSpan(new MemorySpan<ulong>(this.lcActive));

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var vHeightAltered = vHeights[i];
                var baseIndex = i * landClasses.Length;

                for (int itr = 0; itr < landClasses.Length; ++itr)
                {
                    if (!lcActive[baseIndex + itr])
                        continue;
                    double delta = lcDeltas[baseIndex + itr];
                    if (delta <= 0.0)
                        continue;
                    var lc = landClasses[itr];

                    data.vertColor[i] +=
                        Color.Lerp(
                            lc.color,
                            lc.noiseColor,
                            (float)(
                                (double)lc.noiseBlend
                                * lc.noiseSimplex.noiseNormalized(data.directionFromCenter[i])
                            )
                        ) * (float)delta;
                    vHeightAltered += delta * lc.alterApparentHeight;
                }

                data.vertColor[i].a = (float)MathUtil.Clamp01(vHeightAltered);
            }
        }
    }
}
