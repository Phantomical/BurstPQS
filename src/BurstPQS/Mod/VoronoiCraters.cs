using System;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VoronoiCraters : BatchPQSModV1<PQSMod_VoronoiCraters>
{
    public VoronoiCraters(PQSMod_VoronoiCraters mod)
        : base(mod) { }

    float[] rs;

    public override unsafe void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        if (rs is null || rs.Length != data.VertexCount)
            rs = new float[data.VertexCount];

        using var g0 = BurstSimplex.Create(mod.simplex, out var bsimplex);
        using var g1 = BurstAnimationCurve.Create(mod.jitterCurve, out var bjitterCurve);
        using var g2 = BurstAnimationCurve.Create(mod.craterCurve, out var bcraterCurve);

        fixed (float* prs = rs)
        {
            BuildHeights(
                in data.burstData,
                new(mod.voronoi),
                bsimplex,
                bjitterCurve,
                bcraterCurve,
                new(prs, rs.Length),
                mod.jitter,
                mod.jitterHeight,
                mod.deformation
            );
        }
    }

    public override unsafe void OnBatchVertexBuild(in QuadBuildData data)
    {
        if (rs is null || rs.Length != data.VertexCount)
            throw new InvalidOperationException(
                "OnQuadBuildVertex called but rs is null or the wrong size"
            );

        using var g0 = BurstGradient.Create(mod.craterColourRamp, out var bcolorRamp);

        fixed (float* prs = rs)
        {
            BuildVertices(
                in data.burstData,
                in bcolorRamp,
                new(prs, rs.Length),
                mod.rFactor,
                mod.rOffset,
                mod.colorOpacity,
                mod.DebugColorMapping
            );
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstVoronoi voronoi,
        [NoAlias] in BurstSimplex simplex,
        [NoAlias] in BurstAnimationCurve jitterCurve,
        [NoAlias] in BurstAnimationCurve craterCurve,
        [NoAlias] in MemorySpan<float> rs,
        double jitter,
        double jitterHeight,
        double deformation
    )
    {
        if (rs.Length != data.VertexCount)
            BurstException.ThrowIndexOutOfRange();

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

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstGradient craterColorRamp,
        [NoAlias] in MemorySpan<float> rs,
        float rFactor,
        float rOffset,
        float colorOpacity,
        bool debugColorMapping
    )
    {
        if (rs.Length != data.VertexCount)
            BurstException.ThrowIndexOutOfRange();

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
        }
    }
}
