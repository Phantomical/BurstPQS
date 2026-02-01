using System;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexPlanet))]
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

    struct BurstSimplexWrapper(PQSMod_VertexPlanet.SimplexWrapper wrapper)
    {
        public double deformity = wrapper.deformity;
        public double octaves = wrapper.octaves;
        public double persistance = wrapper.persistance;
        public double frequency = wrapper.frequency;
        public BurstSimplex simplex = new(wrapper.simplex);

        public void Dispose() => simplex.Dispose();
    }

    struct BurstNoiseModWrapper<N>(PQSMod_VertexPlanet.NoiseModWrapper wrapper, N noise)
        where N : LibNoise.IModule
    {
        public double deformity = wrapper.deformity;
        public int octaves = wrapper.octaves;
        public double persistance = wrapper.persistance;
        public double frequency = wrapper.frequency;
        public N noise = noise;
    }

    NativeArray<BurstLandClass> landClasses;
    BurstSimplexWrapper continental;
    BurstSimplexWrapper continentalSmoothing;
    BurstSimplexWrapper continentalSharpnessMap;
    BurstSimplexWrapper continentalRuggedness;
    BurstSimplex terrainType;

    public override void OnSetup()
    {
        landClasses = new(mod.landClasses.Length, Allocator.Persistent);

        for (int i = 0; i < mod.landClasses.Length; ++i)
            landClasses[i] = new(mod.landClasses[i]);

        continental = new(mod.continental);
        continentalSmoothing = new(mod.continentalSmoothing);
        continentalSharpnessMap = new(mod.continentalSharpnessMap);
        continentalRuggedness = new(mod.continentalRuggedness);
        terrainType = new(mod.terrainType.simplex);
    }

    public override void Dispose()
    {
        foreach (var lc in landClasses)
            lc.Dispose();
        landClasses.Dispose();

        continental.Dispose();
        continentalSmoothing.Dispose();
        continentalSharpnessMap.Dispose();
        continentalRuggedness.Dispose();
        terrainType.Dispose();
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                landClasses = landClasses,
                continental = continental,
                continentalSmoothing = continentalSmoothing,
                continentalSharpness = new(
                    mod.continentalSharpness,
                    new BurstRidgedMultifractal(
                        (LibNoise.RidgedMultifractal)mod.continentalSharpness.noise
                    )
                ),
                continentalSharpnessMap = continentalSharpnessMap,
                continentalRuggedness = continentalRuggedness,
                terrainType = terrainType,
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
                terrainTypeDeformity = mod.terrainType.deformity,
                buildHeightColors = mod.buildHeightColors,
                colorDeformity = mod.colorDeformity,
            }
        );
    }

    // Running this through burst seems to have different results?
    // The struct does NOT have [BurstCompile] because the heights computation
    // produces different results when burst compiled.
    unsafe struct BuildJob : IBatchPQSHeightJob, IBatchPQSVertexJob, IDisposable
    {
        public NativeArray<BurstLandClass> landClasses;
        public BurstSimplexWrapper continental;
        public BurstSimplexWrapper continentalSmoothing;
        public BurstNoiseModWrapper<BurstRidgedMultifractal> continentalSharpness;
        public BurstSimplexWrapper continentalSharpnessMap;
        public BurstSimplexWrapper continentalRuggedness;
        public BurstSimplex terrainType;
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
        public double terrainTypeDeformity;
        public bool buildHeightColors;
        public double colorDeformity;

        double* preSmoothHeights;

        // csharpier-ignore
        public void BuildHeights(in BuildHeightsData data)
        {
            int vertexCount = data.VertexCount;
            preSmoothHeights = (double*)UnsafeUtility.Malloc(
                vertexCount * sizeof(double),
                UnsafeUtility.AlignOf<double>(),
                Allocator.Temp
            );

            double continentalDeformity;
            double continental2Height;
            double continentialHeight;
            double continentalRHeight;
            double continentialSharpnessValue;
            double continentialSharpnessMapValue;
            double continentalDelta;
            double vHeight;
            double continentialHeightPreSmooth;

            for (int i = 0; i < vertexCount; ++i)
            {
                var directionFromCenter = data.directionFromCenter[i];

                continentalDeformity = 1.0;
                continental2Height = continentalSmoothing.simplex.noiseNormalized(directionFromCenter);
                continental.simplex.persistence = continental.persistance - continentalSmoothing.persistance * continental2Height;
                continentialHeight = continental.simplex.noiseNormalized(directionFromCenter);
                continentialSharpnessValue = (continentalSharpness.noise.GetValue(directionFromCenter) + 1.0) * 0.5;
                continentialSharpnessValue *= Lerp(continentalSharpness.deformity, continentalSharpness.deformity * terrainRidgeBalance, (continental2Height + continentialSharpnessValue) * 0.5);
                continentialSharpnessMapValue = Clamp((continentalSharpnessMap.simplex.noise(directionFromCenter) + 1.0) * 0.5, terrainRidgesMin, terrainRidgesMax);
                continentialSharpnessMapValue = (continentialSharpnessMapValue - terrainRidgesMin) / (terrainRidgesMax - terrainRidgesMin) * continentalSharpnessMap.deformity;
                continentialSharpnessValue += Lerp(0.0, continentialSharpnessValue, continentialSharpnessMapValue);
                continentialHeight += continentialSharpnessValue;
                continentalDeformity += continentalSharpness.deformity * continentalSharpnessMap.deformity;
                continentialHeight /= continentalDeformity;
                continentalDelta = (continentialHeight - oceanLevel) / (1.0 - oceanLevel);

                if (continentialHeight < oceanLevel)
                {
                    if (oceanSnap)
                    {
                        vHeight = 0.0 - oceanStep;
                    }
                    else
                    {
                        vHeight = continentalDelta * oceanDepth - oceanStep;
                    }
                    continentialHeightPreSmooth = vHeight;
                }
                else
                {
                    continentalRuggedness.simplex.persistence = continentalRuggedness.persistance * continentalDelta;
                    continentalRHeight = continentalRuggedness.simplex.noiseNormalized(directionFromCenter) * continentalDelta * continentalDelta;
                    continentialHeight = continentalDelta * continental.deformity + continentalRHeight * continentalRuggedness.deformity;
                    continentialHeight /= continental.deformity + continentalRuggedness.deformity;
                    continentialHeightPreSmooth = continentialHeight;
                    continentialHeight = CubicHermite(0.0, 1.0, terrainShapeStart, terrainShapeEnd, continentialHeight);
                    vHeight = continentialHeight;
                }

                data.vertHeight[i] += Math.Round(vHeight, 5) * deformity;
                preSmoothHeights[i] = continentialHeightPreSmooth;
            }
        }

        public void BuildVertices(in BuildVerticesData data)
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
                    (preSmoothHeights[i] + d1 * terrainTypeDeformity) * h
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
                data.vertColor[i].a = (float)preSmoothHeights[i];
            }
        }

        public void Dispose()
        {
            if (preSmoothHeights != null)
            {
                UnsafeUtility.Free(preSmoothHeights, Allocator.Temp);
                preSmoothHeights = null;
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

    static double Lerp(double v2, double v1, double dt)
    {
        return v1 * dt + v2 * (1.0 - dt);
    }

    static double Clamp(double v, double low, double high)
    {
        if (v < low)
        {
            return low;
        }
        if (v > high)
        {
            return high;
        }
        return v;
    }

    static double CubicHermite(
        double start,
        double end,
        double startTangent,
        double endTangent,
        double t
    )
    {
        double ct2 = t * t;
        double ct3 = ct2 * t;
        return start * (2.0 * ct3 - 3.0 * ct2 + 1.0)
            + startTangent * (ct3 - 2.0 * ct2 + t)
            + end * (-2.0 * ct3 + 3.0 * ct2)
            + endTangent * (ct3 - ct2);
    }
}
