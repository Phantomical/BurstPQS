using System;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexPlanet : PQSMod_VertexPlanet, IBatchPQSMod
{
    struct BurstLandClass(LandClass lc, BurstSimplex simplex)
    {
        public double fractalStart = lc.fractalStart;
        public double fractalEnd = lc.fractalEnd;
        public Color baseColor = lc.baseColor;
        public Color colorNoise = lc.colorNoise;
        public double colorNoiseAmount = lc.colorNoiseAmount;
        public BurstSimplex colorNoiseMap = simplex;
        public bool lerpToNext = lc.lerpToNext;
    }

    BurstLandClass[] burstLandClasses;
    BurstSimplex.Guard[] landClassGuards;
    double[] preSmoothHeights;

    public override void OnSetup()
    {
        base.OnSetup();

        DisposeGuards();
        burstLandClasses = new BurstLandClass[landClasses.Length];
        landClassGuards = new BurstSimplex.Guard[landClasses.Length];

        for (int i = 0; i < landClasses.Length; ++i)
        {
            var lc = landClasses[i];
            landClassGuards[i] = BurstSimplex.Create(lc.colorNoiseMap.simplex, out var simplex);
            burstLandClasses[i] = new(lc, simplex);
        }
    }

    public unsafe void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        if (preSmoothHeights is null || preSmoothHeights.Length != data.VertexCount)
            preSmoothHeights = new double[data.VertexCount];

        using var g0 = BurstSimplex.Create(this.continental.simplex, out var continental);
        using var g1 = BurstSimplex.Create(
            this.continentalSmoothing.simplex,
            out var continentalSmoothing
        );
        using var g2 = BurstSimplex.Create(
            this.continentalSharpnessMap.simplex,
            out var continentalSharpnessMap
        );
        using var g3 = BurstSimplex.Create(
            this.continentalRuggedness.simplex,
            out var continentalRuggedness
        );

        fixed (double* pPreSmoothHeights = preSmoothHeights)
        {
            BuildHeight(
                in data.burstData,
                new(pPreSmoothHeights, preSmoothHeights.Length),
                in continental,
                in continentalSmoothing,
                new((LibNoise.RidgedMultifractal)continentalSharpness.noise),
                continentalSharpness.deformity,
                in continentalSharpnessMap,
                this.continentalSharpnessMap.deformity,
                in continentalRuggedness,
                this.continentalRuggedness.deformity,
                terrainRidgeBalance,
                terrainRidgesMax,
                terrainRidgesMin,
                terrainShapeStart,
                terrainShapeEnd,
                oceanLevel,
                oceanDepth,
                oceanStep,
                oceanSnap,
                deformity
            );
        }
    }

    public unsafe void OnQuadBuildVertex(in QuadBuildData data)
    {
        using var g0 = BurstSimplex.Create(this.terrainType.simplex, out var terrainType);

        fixed (BurstLandClass* pLandClasses = burstLandClasses)
        fixed (double* pPreSmoothHeights = preSmoothHeights)
        {
            BuildVertex(
                in data.burstData,
                new(pLandClasses, burstLandClasses.Length),
                new(pPreSmoothHeights, preSmoothHeights.Length),
                in terrainType,
                this.terrainType.deformity,
                buildHeightColors,
                sphere.radius,
                colorDeformity
            );
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeight(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in MemorySpan<double> preSmoothHeights,
        [NoAlias] in BurstSimplex iContinental,
        [NoAlias] in BurstSimplex continentalSmoothing,
        [NoAlias] in RidgedMultifractal continentalSharpness,
        double continentalSharpnessDeformity,
        [NoAlias] in BurstSimplex continentalSharpnessMap,
        double continentalSharpnessMapDeformity,
        [NoAlias] in BurstSimplex iContinentalRuggedness,
        double continentalRuggednessDeformity,
        double terrainRidgeBalance,
        double terrainRidgesMax,
        double terrainRidgesMin,
        double terrainShapeStart,
        double terrainShapeEnd,
        double oceanLevel,
        double oceanDepth,
        double oceanStep,
        bool oceanSnap,
        double deformity
    )
    {
        var continental = iContinental;
        var continentalRuggedness = iContinentalRuggedness;

        double originalContinentalPersistence = continental.persistence;
        double originalContinentalRuggednessPersistence = continentalRuggedness.persistence;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            var dir = data.directionFromCenter[i];
            double continentalDeformity = 1.0;
            double continental2Height = continentalSmoothing.noiseNormalized(dir);
            continental.persistence =
                originalContinentalPersistence
                - continentalSmoothing.persistence * continental2Height;
            double continentialHeight = continental.noiseNormalized(dir);
            double continentialSharpnessValue = (continentalSharpness.GetValue(dir) + 1.0) * 0.5;
            continentialSharpnessValue *= Lerp(
                continentalSharpnessDeformity,
                continentalSharpnessDeformity * terrainRidgeBalance,
                (continental2Height + continentialSharpnessValue) * 0.5
            );
            double continentialSharpnessMapValue = MathUtil.Clamp(
                (continentalSharpnessMap.noise(dir) + 1.0) * 0.5,
                terrainRidgesMin,
                terrainRidgesMax
            );
            continentialSharpnessValue += Lerp(
                0.0,
                continentialSharpnessValue,
                continentialSharpnessMapValue
            );
            continentialHeight += continentialSharpnessValue;
            continentalDeformity +=
                continentalSharpnessDeformity * continentalSharpnessMapDeformity;
            continentialHeight /= continentalDeformity;
            double continentalDelta = (continentialHeight - oceanLevel) / (1.0 - oceanLevel);
            double vHeight;
            double continentialHeightPreSmooth;

            if (continentialHeight < oceanLevel)
            {
                if (oceanSnap)
                    vHeight = -oceanStep;
                else
                    vHeight = continentalDelta * oceanDepth - oceanStep;
                continentialHeightPreSmooth = vHeight;
            }
            else
            {
                continentalRuggedness.persistence =
                    originalContinentalRuggednessPersistence * continentalDelta;
                double continentalRHeight =
                    continentalRuggedness.noiseNormalized(dir)
                    * continentalDelta
                    * continentalDelta;
                continentialHeight =
                    continentalDelta * continentalDeformity
                    + continentalRHeight * continentalRuggednessDeformity;
                continentialHeight /= continentalDeformity + continentalRuggednessDeformity;
                continentialHeightPreSmooth = continentialHeight;
                continentialHeight = MathUtil.CubicHermite(
                    0.0,
                    1.0,
                    terrainShapeStart,
                    terrainShapeEnd,
                    continentialHeight
                );
                vHeight = continentialHeight;
            }

            data.vertHeight[i] = Math.Round(vHeight, 5) * deformity;
            preSmoothHeights[i] = continentialHeightPreSmooth;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertex(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in MemorySpan<BurstLandClass> landClasses,
        [NoAlias] in MemorySpan<double> continentalHeightPreSmooth,
        [NoAlias] in BurstSimplex terrainType,
        double terrainTypeDeformity,
        bool buildHeightColors,
        double sphereRadius,
        double colorDeformity
    )
    {
        if (buildHeightColors)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                float h = (float)((data.vertHeight[i] - sphereRadius) / colorDeformity);
                data.vertColor[i] = new Color(h, h, h, data.vertColor[i].a);
            }

            return;
        }

        for (int i = 0; i < data.VertexCount; ++i)
        {
            var dir = data.directionFromCenter[i];
            double h = (data.vertHeight[i] - sphereRadius) / colorDeformity;
            double d1 = terrainType.noiseNormalized(dir);
            double tHeight = MathUtil.Clamp01(
                (continentalHeightPreSmooth[i] + d1 * terrainTypeDeformity) * h
            );

            int lcSelectedIndex = SelectLandClassByHeight(landClasses, tHeight);
            ref var lcSelected = ref landClasses[lcSelectedIndex];

            var c1 = Color.Lerp(
                lcSelected.baseColor,
                lcSelected.colorNoise,
                (float)(lcSelected.colorNoiseAmount * lcSelected.colorNoiseMap.noiseNormalized(dir))
            );

            if (lcSelected.lerpToNext)
            {
                ref var lcLerp = ref landClasses[lcSelectedIndex + 1];
                var c2 = Color.Lerp(
                    lcLerp.baseColor,
                    lcLerp.colorNoise,
                    (float)(lcLerp.colorNoiseAmount * lcLerp.colorNoiseMap.noiseNormalized(dir))
                );
                c1 = Color.Lerp(
                    c1,
                    c2,
                    (float)(
                        (tHeight - lcSelected.fractalStart)
                        / (lcSelected.fractalEnd - lcSelected.fractalStart)
                    )
                );
            }

            data.vertColor[i] = c1;
            data.vertColor[i].a = (float)continentalHeightPreSmooth[i];
        }
    }

    static int SelectLandClassByHeight(MemorySpan<BurstLandClass> lcs, double height)
    {
        for (int i = 0; i < lcs.Length; ++i)
        {
            if (lcs[i].fractalStart <= height && height < lcs[i].fractalEnd)
                return i;
        }

        return 0;
    }

    public void OnDestroy() => DisposeGuards();

    void DisposeGuards()
    {
        if (landClassGuards is null)
            return;

        foreach (var guard in landClassGuards)
            guard.Dispose();
        landClassGuards = null;
    }
}
