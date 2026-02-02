using System;
using BurstPQS.Collections;
using BurstPQS.Map;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Mod;

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
    BurstSimplex altitudeSimplex;
    BurstSimplex latitudeSimplex;
    BurstSimplex longitudeSimplex;

    public override void OnSetup()
    {
        burstLandClasses = new NativeArray<BurstLandClass>(
            mod.landClasses.Length,
            Allocator.Persistent
        );
        for (int i = 0; i < mod.landClasses.Length; ++i)
            burstLandClasses[i] = new(mod.landClasses[i]);

        altitudeSimplex = new(mod.altitudeSimplex);
        latitudeSimplex = new(mod.latitudeSimplex);
        longitudeSimplex = new(mod.longitudeSimplex);
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                landClasses = burstLandClasses,
                altitudeSimplex = altitudeSimplex,
                latitudeSimplex = latitudeSimplex,
                longitudeSimplex = longitudeSimplex,

                heightMap = mod.useHeightMap ? BurstMapSO.Create(mod.heightMap) : null,
                altitudeBlend = mod.altitudeBlend,
                latitudeBlend = mod.latitudeBlend,
                longitudeBlend = mod.longitudeBlend,
                vHeightMax = mod.vHeightMax,

                createColors = mod.createColors,
                scatterActive = mod.scatterActive,
                mod = mod,
            }
        );
    }

    public override void Dispose()
    {
        altitudeSimplex.Dispose();
        latitudeSimplex.Dispose();
        longitudeSimplex.Dispose();

        using var blcs = burstLandClasses;
        foreach (var blc in burstLandClasses)
            blc.Dispose();
    }

    struct BuildJob : IBatchPQSHeightJob, IBatchPQSVertexJob, IBatchPQSMeshBuiltJob, IDisposable
    {
        public NativeArray<BurstLandClass> landClasses;
        public BurstSimplex altitudeSimplex;
        public BurstSimplex latitudeSimplex;
        public BurstSimplex longitudeSimplex;

        public BurstMapSO? heightMap;
        public double altitudeBlend;
        public double latitudeBlend;
        public double longitudeBlend;
        public double vHeightMax;

        public bool createColors;
        public bool scatterActive;
        public PQSLandControl mod;

        // Cross-phase state allocated in BuildHeights, used through OnMeshBuilt
        NativeArray<ulong> lcActive;
        NativeArray<double> lcDeltas;
        NativeArray<double> vHeights;
        NativeArray<bool> allowScatterCopy;

        public void BuildHeights(in BuildHeightsData data)
        {
            int landClassCount = landClasses.Length;
            int lcActiveCount = data.VertexCount * landClasses.Length;
            int lcActiveUlongCount = (lcActiveCount + 63) / 64;

            lcActive = new(lcActiveUlongCount, Allocator.TempJob);
            lcDeltas = new(lcActiveCount, Allocator.TempJob);
            vHeights = new(data.VertexCount, Allocator.TempJob);

            var lcActiveBits = new BitSpan(new MemorySpan<ulong>(lcActive));
            var lcDeltasSpan = new MemorySpan<double>(lcDeltas);
            var vHeightsSpan = new MemorySpan<double>(vHeights);

            lcActiveBits.Clear();
            lcDeltasSpan.Clear();
            vHeightsSpan.Clear();

            double sphereRadius = data.sphere.radius;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                double totalDelta = 0.0;
                double vHeight;

                if (this.heightMap is BurstMapSO hMap)
                    vHeight = hMap.GetPixelFloat(data.u[i], data.v[i]);
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

                int baseIndex = landClassCount * i;
                for (int itr = 0; itr < landClassCount; ++itr)
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
                        lcActiveBits[baseIndex + itr] = true;
                        totalDelta += delta;
                    }
                }

                for (int itr = 0; itr < landClassCount; ++itr)
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

            heightMap?.Dispose();
            heightMap = null;
        }

        public void BuildVertices(in BuildVerticesData data)
        {
            int landClassCount = landClasses.Length;
            var lcActiveBits = new BitSpan(new MemorySpan<ulong>(lcActive));

            if (createColors)
            {
                for (int i = 0; i < data.VertexCount; ++i)
                {
                    var vHeightAltered = vHeights[i];
                    var baseIndex = i * landClassCount;

                    for (int itr = 0; itr < landClassCount; ++itr)
                    {
                        if (!lcActiveBits[baseIndex + itr])
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

            // Copy allowScatter for use in OnMeshBuilt
            if (scatterActive)
            {
                allowScatterCopy = new(data.VertexCount, Allocator.Temp);
                for (int i = 0; i < data.VertexCount; ++i)
                    allowScatterCopy[i] = data.allowScatter[i];
            }
        }

        public void OnMeshBuilt(PQ quad)
        {
            if (!scatterActive)
                return;

            var vertexCount = vHeights.Length;
            var landClassCount = landClasses.Length;
            var lcActiveBits = new BitSpan(new MemorySpan<ulong>(lcActive));

            for (int i = 0; i < vertexCount; ++i)
            {
                var baseIndex = i * landClassCount;

                for (int itr = 0; itr < landClassCount; ++itr)
                {
                    if (!lcActiveBits[baseIndex + itr])
                        continue;
                    double delta = lcDeltas[baseIndex + itr];
                    if (delta <= 0.0)
                        continue;

                    var lc = mod.landClasses[itr];
                    if (!allowScatterCopy[i] || delta <= 0.05)
                        continue;

                    foreach (var landClassScatterAmount in lc.scatter)
                    {
                        if (quad.subdivision >= mod.scatterMinSubdiv)
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
        }

        public void Dispose()
        {
            if (heightMap is BurstMapSO hMap)
                hMap.Dispose();

            lcActive.Dispose();
            lcDeltas.Dispose();
            vHeights.Dispose();
            allowScatterCopy.Dispose();
        }
    }
}
