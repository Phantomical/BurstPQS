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
public class VertexPlanet(PQSMod_VertexPlanet mod) : BatchPQSMod<PQSMod_VertexPlanet>(mod)
{
    struct BurstLandClass(PQSMod_VertexPlanet.LandClass lc) : IDisposable
    {
        public double fractalStart = lc.fractalStart;
        public double fractalEnd = lc.fractalEnd;
        public Color baseColor = lc.baseColor;
        public Color colorNoise = lc.colorNoise;
        public double colorNoiseAmount = lc.colorNoiseAmount;
        public BurstSimplex colorNoiseMap = new(lc.colorNoiseMap.simplex);
        public bool lerpToNext = lc.lerpToNext;

        public readonly void Dispose()
        {
            colorNoiseMap.Dispose();
        }
    }

    NativeArray<BurstLandClass> landClasses;

    public override void OnSetup()
    {
        landClasses = new(mod.landClasses.Length, Allocator.Persistent);

        for (int i = 0; i < mod.landClasses.Length; ++i)
            landClasses[i] = new(mod.landClasses[i]);
    }

    public override void Dispose()
    {
        foreach (var lc in landClasses)
            lc.Dispose();
        landClasses.Dispose();
    }

    class State(QuadBuildData data, VertexPlanet batchMod) : BatchPQSModState
    {
        public PQSMod_VertexPlanet mod = batchMod.mod;
        public NativeArray<double> preSmoothHeights = new(data.VertexCount, Allocator.TempJob);
        public NativeArray<BurstLandClass> landClasses = batchMod.landClasses;

        public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
        {
            var job = new BuildHeightsJob
            {
                data = data.burst,
                preSmoothHeights = preSmoothHeights,
                continental = new(mod.continental.simplex),
                continentalSmoothing = new(mod.continentalSmoothing.simplex),
                continentalSharpness = new(
                    (LibNoise.RidgedMultifractal)mod.continentalSharpness.noise
                ),
                continentalSharpnessDeformity = mod.continentalSharpness.deformity,
                continentalSharpnessMap = new(mod.continentalSharpnessMap.simplex),
                continentalSharpnessMapDeformity = mod.continentalSharpnessMap.deformity,
                continentalRuggedness = new(mod.continentalRuggedness.simplex),
                continentalRuggednessDeformity = mod.continentalRuggedness.deformity,
                terrainRidgeBalance = mod.terrainRidgeBalance,
                terrainRidgesMax = mod.terrainRidgesMax,
                terrainRidgesMin = mod.terrainRidgesMin,
                terrainShapeStart = mod.terrainShapeStart,
                terrainShapeEnd = mod.terrainShapeEnd,
                oceanLevel = mod.oceanLevel,
                oceanDepth = mod.oceanDepth,
                oceanStep = mod.oceanStep,
                oceanSnap = mod.oceanSnap,
                deformity = mod.deformity,
            };

            handle = job.Schedule(handle);
            job.continental.Dispose(handle);
            job.continentalSmoothing.Dispose(handle);
            job.continentalSharpnessMap.Dispose(handle);
            job.continentalRuggedness.Dispose(handle);

            return handle;
        }

        public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
        {
            var job = new BuildVerticesJob
            {
                data = data.burst,
                landClasses = landClasses,
                continentalHeightPreSmooth = preSmoothHeights,
                terrainType = new(mod.terrainType.simplex),
                terrainTypeDeformity = mod.terrainType.deformity,
                buildHeightColors = mod.buildHeightColors,
                colorDeformity = mod.colorDeformity,
            };

            handle = job.Schedule(handle);
            job.terrainType.Dispose(handle);

            return handle;
        }

        public override void Dispose()
        {
            preSmoothHeights.Dispose();
        }
    }

    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public NativeArray<double> preSmoothHeights;
        public BurstSimplex continental;
        public BurstSimplex continentalSmoothing;
        public RidgedMultifractal continentalSharpness;
        public double continentalSharpnessDeformity;
        public BurstSimplex continentalSharpnessMap;
        public double continentalSharpnessMapDeformity;
        public BurstSimplex continentalRuggedness;
        public double continentalRuggednessDeformity;
        public double terrainRidgeBalance;
        public double terrainRidgesMax;
        public double terrainRidgesMin;
        public double terrainShapeStart;
        public double terrainShapeEnd;
        public double oceanLevel;
        public double oceanDepth;
        public double oceanStep;
        public bool oceanSnap;
        public double deformity;

        public void Execute()
        {
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
                double continentialSharpnessValue =
                    (continentalSharpness.GetValue(dir) + 1.0) * 0.5;
                continentialSharpnessValue *= MathUtil.Lerp(
                    continentalSharpnessDeformity,
                    continentalSharpnessDeformity * terrainRidgeBalance,
                    (continental2Height + continentialSharpnessValue) * 0.5
                );
                double continentialSharpnessMapValue = MathUtil.Clamp(
                    (continentalSharpnessMap.noise(dir) + 1.0) * 0.5,
                    terrainRidgesMin,
                    terrainRidgesMax
                );
                continentialSharpnessValue += MathUtil.Lerp(
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
    }

    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public NativeArray<BurstLandClass> landClasses;
        public NativeArray<double> continentalHeightPreSmooth;
        public BurstSimplex terrainType;
        public double terrainTypeDeformity;
        public bool buildHeightColors;
        public double colorDeformity;

        public void Execute()
        {
            if (buildHeightColors)
            {
                for (int i = 0; i < data.VertexCount; ++i)
                {
                    float h = (float)((data.vertHeight[i] - data.sphere.radius) / colorDeformity);
                    data.vertColor[i] = new Color(h, h, h, data.vertColor[i].a);
                }

                return;
            }

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var dir = data.directionFromCenter[i];
                double h = (data.vertHeight[i] - data.sphere.radius) / colorDeformity;
                double d1 = terrainType.noiseNormalized(dir);
                double tHeight = MathUtil.Clamp01(
                    (continentalHeightPreSmooth[i] + d1 * terrainTypeDeformity) * h
                );

                int lcSelectedIndex = SelectLandClassByHeight(new(landClasses), tHeight);
                var lcSelected = landClasses[lcSelectedIndex];

                var c1 = Color.Lerp(
                    lcSelected.baseColor,
                    lcSelected.colorNoise,
                    (float)(
                        lcSelected.colorNoiseAmount * lcSelected.colorNoiseMap.noiseNormalized(dir)
                    )
                );

                if (lcSelected.lerpToNext)
                {
                    var lcLerp = landClasses[lcSelectedIndex + 1];
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
}
