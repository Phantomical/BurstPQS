using System;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VoronoiCraters2 : PQSMod_VoronoiCraters2, IBatchPQSMod
{
    float[] rs;

    public unsafe void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        if (rs is null || rs.Length != data.VertexCount)
            rs = new float[data.VertexCount];

        using var g0 = BurstSimplex.Create(jitterSimplex, out var bjitterSimplex);
        using var g1 = BurstAnimationCurve.Create(craterCurve, out var bcraterCurve);
        using var g2 = BurstSimplex.Create(deformationSimplex, out var bdeformationSimplex);

        fixed (float* prs = rs)
        {
            BuildHeights(
                in data.burstData,
                new(voronoi),
                in bjitterSimplex,
                in bcraterCurve,
                in bdeformationSimplex,
                new(prs, rs.Length),
                jitter,
                deformation
            );
        }
    }

    public unsafe void OnQuadBuildVertex(in QuadBuildData data)
    {
        if (rs is null || rs.Length != data.VertexCount)
            throw new InvalidOperationException(
                "OnQuadBuildVertex called but rs is null or the wrong size"
            );

        using var g0 = BurstGradient.Create(craterColourRamp, out var bcolorRamp);

        fixed (float* prs = rs)
        {
            BuildVertices(in data.burstData, in bcolorRamp, new(prs, rs.Length), DebugColorMapping);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstVoronoi voronoi,
        [NoAlias] in BurstSimplex jitterSimplex,
        [NoAlias] in BurstAnimationCurve craterCurve,
        [NoAlias] in BurstSimplex deformationSimplex,
        [NoAlias] in MemorySpan<float> rs,
        double jitter,
        double deformation
    )
    {
        if (rs.Length != data.VertexCount)
            BurstException.ThrowIndexOutOfRange();

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

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstGradient craterColorRamp,
        [NoAlias] in MemorySpan<float> rs,
        bool debugColorMapping
    )
    {
        if (rs.Length != data.VertexCount)
            BurstException.ThrowIndexOutOfRange();

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
