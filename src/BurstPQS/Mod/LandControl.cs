using System;
using System.Collections.Generic;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSLandControl))]
public class LandControl : BatchPQSModV1<PQSLandControl>
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
            if (point <= startStart || point >= endEnd)
                return 0.0;

            if (point < startEnd)
                return (point - startStart) * startDelta;
            if (point <= endStart)
                return 1.0;
            if (point < endEnd)
                return (point - endStart) * endDelta;
            return 0.0;
        }
    }

    public struct BurstLandClass(PQSLandControl.LandClass lc)
    {
        public BurstLerpRange altitudeRange = new(lc.altitudeRange);
        public BurstLerpRange latitudeRange = new(lc.latitudeRange);
        public bool latitudeDouble = lc.latitudeDouble;
        public BurstLerpRange latitudeDoubleRange = new(lc.latitudeDoubleRange);
        public BurstLerpRange longitudeRange = new(lc.longitudeRange);
        public BurstSimplex coverageSimplex;
        public BurstSimplex noiseSimplex;
        public double minimumRealHeight = lc.minimumRealHeight;
        public double alterRealHeight = lc.alterRealHeight;
        public double coverageBlend = lc.coverageBlend;
    }

    public LandControl(PQSLandControl mod)
        : base(mod) { }

    BurstLandClass[] burstLandClasses;
    List<BurstSimplex.Guard> guards;

    ulong[] lcActive;
    double[] lcDeltas;
    double[] vHeights;

    public override void OnSetup()
    {
        base.OnSetup();

        burstLandClasses = new BurstLandClass[mod.landClasses.Length];
        guards = [];
        for (int i = 0; i < mod.landClasses.Length; ++i)
        {
            var lc = mod.landClasses[i];
            ref var blc = ref burstLandClasses[i];

            blc = new(lc);

            guards.Add(BurstSimplex.Create(lc.coverageSimplex, out blc.coverageSimplex));
            guards.Add(BurstSimplex.Create(lc.noiseSimplex, out blc.noiseSimplex));
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        foreach (var guard in guards)
            guard.Dispose();
    }

    public override unsafe void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        int lcActiveCount = data.VertexCount * burstLandClasses.Length;
        if (lcActive is null || lcActive.Length != lcActiveCount)
            lcActive = new ulong[(lcActiveCount + 63) / 63];
        if (lcDeltas is null || lcDeltas.Length != lcActiveCount)
            lcDeltas = new double[lcActiveCount];
        if (vHeights is null || vHeights.Length != data.VertexCount)
            vHeights = new double[data.VertexCount];

        using var g0 = BurstMapSO.Create(mod.heightMap, out var bheightMap);
        using var g1 = BurstSimplex.Create(mod.altitudeSimplex, out var baltitudeSimplex);
        using var g2 = BurstSimplex.Create(mod.latitudeSimplex, out var blatitudeSimplex);
        using var g3 = BurstSimplex.Create(mod.longitudeSimplex, out var blongitudeSimplex);

        fixed (ulong* plcActive = lcActive)
        fixed (double* plcDeltas = lcDeltas)
        fixed (double* pvHeights = vHeights)
        fixed (BurstLandClass* pLandClasses = burstLandClasses)
        {
            BuildHeights(
                in data.burstData,
                new(pLandClasses, burstLandClasses.Length),
                new(plcActive, lcActive.Length),
                new(plcDeltas, lcDeltas.Length),
                new(pvHeights, vHeights.Length),
                in bheightMap,
                in baltitudeSimplex,
                in blatitudeSimplex,
                in blongitudeSimplex,
                mod.altitudeBlend,
                mod.latitudeBlend,
                mod.longitudeBlend,
                mod.sphere.radius,
                mod.vHeightMax,
                mod.useHeightMap
            );
        }
    }

    public override unsafe void OnBatchVertexBuild(in QuadBuildDataV1 data)
    {
        fixed (ulong* plcActive = lcActive)
        {
            BuildVertices(data, new(plcActive, lcActive.Length), lcDeltas, vHeights);
        }
    }

    [BurstCompile]
    static void BuildHeights(
        in BurstQuadBuildDataV1 data,
        in MemorySpan<BurstLandClass> landClasses,
        in BitSpan lcActive,
        in MemorySpan<double> lcDeltas,
        in MemorySpan<double> vHeights,
        in BurstMapSO heightMap,
        in BurstSimplex altitudeSimplex,
        in BurstSimplex latitudeSimplex,
        in BurstSimplex longitudeSimplex,
        double altitudeBlend,
        double latitudeBlend,
        double longitudeBlend,
        double sphereRadius,
        double vHeightMax,
        bool useHeightMap
    )
    {
        lcActive.Clear();
        lcDeltas.Clear();
        vHeights.Clear();

        for (int i = 0; i < data.VertexCount; ++i)
        {
            double totalDelta = 0.0;
            double vHeight;

            data.vertColor[i] = Color.black;
            if (useHeightMap)
                vHeight = heightMap.GetPixelFloat(data.u[i], data.v[i]);
            else
                vHeight = (data.vertHeight[i] - sphereRadius) / vHeightMax;
            vHeight += altitudeBlend * altitudeSimplex.noise(data.directionFromCenter[i]);
            vHeight = Math.Min(vHeight, 1.0);

            double vLat =
                data.sy[i] + latitudeBlend * latitudeSimplex.noise(data.directionFromCenter[i]);
            double vLon =
                data.sx[i] + longitudeBlend * longitudeSimplex.noise(data.directionFromCenter[i]);

            vLat = MathUtil.Clamp01(vLat);
            vLon = MathUtil.Clamp01(vLon);
            vHeights[i] = vHeight;

            int baseIndex = landClasses.Length * i;
            for (int itr = 0; itr < landClasses.Length; ++itr)
            {
                ref readonly var lc = ref landClasses[itr];
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

                lcDeltas[baseIndex + itr] = delta;
                lcActive[baseIndex + itr] = delta == 0.0;
                totalDelta += delta;
            }

            for (int itr = 0; itr < landClasses.Length; ++itr)
            {
                ref readonly var lc = ref landClasses[itr];
                lcDeltas[baseIndex + itr] /= totalDelta;
                double delta = lcDeltas[baseIndex + itr];

                if (delta <= 0.0)
                    continue;

                if (
                    lc.minimumRealHeight != 0.0
                    && data.vertHeight[i] - sphereRadius < lc.minimumRealHeight
                )
                    data.vertHeight[i] = sphereRadius + delta * lc.minimumRealHeight;

                data.vertHeight[i] += delta * lc.alterRealHeight;
            }
        }
    }

    void BuildVertices(
        QuadBuildDataV1 data,
        in BitSpan lcActive,
        double[] lcDeltas,
        double[] vHeights
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var vHeightAltered = vHeights[i];
            var baseIndex = i * burstLandClasses.Length;

            for (int itr = 0; itr < burstLandClasses.Length; ++itr)
            {
                if (!lcActive[baseIndex + itr])
                    continue;
                double delta = lcDeltas[baseIndex + itr];
                if (delta <= 0.0)
                    continue;
                var lc = mod.landClasses[itr];

                if (mod.createColors)
                {
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

                if (data.allowScatter[i] && mod.scatterActive && delta > 0.05)
                {
                    var scatter = lc.scatter;
                    foreach (var landClassScatterAmount in scatter)
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

            if (!mod.createColors)
                continue;

            data.vertColor[i].a = (float)MathUtil.Clamp01(vHeightAltered);
        }
    }
}
