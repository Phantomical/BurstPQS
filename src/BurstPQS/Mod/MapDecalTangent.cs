using System;
using System.Runtime.CompilerServices;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_MapDecalTangent))]
public class MapDecalTangent(PQSMod_MapDecalTangent mod) : BatchPQSMod<PQSMod_MapDecalTangent>(mod)
{
    class State(PQSMod_MapDecalTangent mod) : BatchPQSModState
    {
        BurstInfo info = new(mod);
        NativeArray<bool> vertActive;

        public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
        {
            vertActive = new(
                data.VertexCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );

            BurstMapSO? heightMap = null;
            if (mod.heightMap is not null)
                heightMap = new BurstMapSO(mod.heightMap);

            var job = new BuildHeightsJob
            {
                data = data.burst,
                info = info,
                heightMap = heightMap,
                sphereIsBuildingMaps = mod.sphere.isBuildingMaps,
                sphereRadius = mod.sphere.radius,
                vertActive = vertActive,
            };
            handle = job.Schedule(handle);
            heightMap?.Dispose(handle);

            return handle;
        }

        public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
        {
            BurstMapSO? colorMap = null;
            if (mod.colorMap is not null)
                colorMap = new BurstMapSO(mod.colorMap);

            var job = new BuildVerticesJob
            {
                data = data.burst,
                info = info,
                colorMap = colorMap,
                sphereRadius = mod.sphere.radius,
                vertActive = vertActive,
            };
            handle = job.Schedule(handle);
            colorMap?.Dispose(handle);
            vertActive.Dispose(handle);

            return base.ScheduleBuildHeights(data, handle);
        }

        public override void OnQuadBuilt(QuadBuildData data)
        {
            mod.OnQuadBuilt(data.buildQuad);
        }

        public override void Dispose()
        {
            vertActive.Dispose();
        }
    }

    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data)
    {
        mod.OnQuadPreBuild(data.buildQuad);
        return new State(mod);
    }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstInfo info;
        public BurstMapSO? heightMap;
        public bool sphereIsBuildingMaps;
        public double sphereRadius;
        public NativeArray<bool> vertActive;

        public void Execute()
        {
            info.BuildHeights(data, heightMap, sphereIsBuildingMaps, sphereRadius, vertActive);
        }
    }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstInfo info;
        public BurstMapSO? colorMap;
        public NativeArray<bool> vertActive;
        public double sphereRadius;

        public void Execute()
        {
            info.BuildVerts(data, colorMap, vertActive, sphereRadius);
        }
    }

    struct BurstInfo(PQSMod_MapDecalTangent mod)
    {
        public double radius = mod.radius;

        public double heightMapDeformity = mod.heightMapDeformity;

        public bool cullBlack = mod.cullBlack;

        public bool useAlphaHeightSmoothing = mod.useAlphaHeightSmoothing;

        public bool absolute = mod.absolute;

        public double absoluteOffset = mod.absoluteOffset;

        public float smoothHeight = mod.smoothHeight;

        public float smoothColor = mod.smoothColor;

        public bool removeScatter = mod.removeScatter;

        public bool DEBUG_HighlightInclusion = mod.DEBUG_HighlightInclusion;

        public double inclusionAngle = mod.inclusionAngle;

        public bool quadActive = mod.quadActive;

        public Vector3d posNorm = mod.posNorm;

        public Quaternion rot = mod.rot;
        public bool buildHeight = mod.buildHeight;
        public float smoothCR = mod.smoothCR;

        public float smoothC1M = mod.smoothC1M;

        public float smoothHR = mod.smoothHR;

        public float smoothH1M = mod.smoothH1M;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void BuildHeights(
            in BurstQuadBuildData data,
            BurstMapSO? nHeightMap,
            bool sphereIsBuildingMaps,
            double sphereRadius,
            NativeArray<bool> vertActive
        )
        {
            vertActive.Clear();

            for (int i = 0; i < data.VertexCount; ++i)
            {
                if (sphereIsBuildingMaps)
                {
                    var quadAngle = Math.Acos(Vector3d.Dot(data.directionFromCenter[i], posNorm));
                    if (quadAngle > inclusionAngle)
                        continue;
                }

                var vertRot = rot * data.directionFromCenter[i];
                var u = (float)((vertRot.x * sphereRadius / radius + 1.0) * 0.5);
                var v = (float)((vertRot.z * sphereRadius / radius + 1.0) * 0.5);

                if (u < 0f || u > 1f || v < 0f || v > 1f)
                    continue;

                if (buildHeight || sphereIsBuildingMaps)
                    vertActive[i] = true;
                if (nHeightMap is not BurstMapSO heightMap)
                    return;

                var ha = heightMap.GetPixelHeightAlpha(u, v);
                var smoothFactor = GetHeightSmoothing(u, v);
                if (useAlphaHeightSmoothing)
                    smoothFactor *= ha.alpha;
                if (!(smoothFactor > 0f))
                    continue;
                if (removeScatter)
                    data.allowScatter[i] = false;

                var height =
                    heightMapDeformity
                    * ha.height
                    / Vector3d.Dot(data.directionFromCenter[i], posNorm);

                if (absolute)
                    height += sphereRadius + absoluteOffset;

                if (cullBlack && ha.height <= 0f)
                {
                    // do nothing
                }
                else
                {
                    data.vertHeight[i] = MathUtil.Lerp(data.vertHeight[i], height, smoothFactor);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void BuildVerts(
            in BurstQuadBuildData data,
            BurstMapSO? nColorMap,
            NativeArray<bool> vertActive,
            double sphereRadius
        )
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                if (!vertActive[i])
                {
                    if (DEBUG_HighlightInclusion && quadActive)
                        data.vertColor[i] = Color.red;
                    continue;
                }

                var vertRot = rot * data.directionFromCenter[i];
                var u = (float)((vertRot.x * sphereRadius / radius + 1.0) * 0.5);
                var v = (float)((vertRot.z * sphereRadius / radius + 1.0) * 0.5);

                if (DEBUG_HighlightInclusion)
                {
                    data.vertColor[i] = Color.green;
                }
                else if (nColorMap is BurstMapSO colorMap)
                {
                    var c1 = colorMap.GetPixelColor(u, v);
                    var smoothFactor = c1.a * GetColorSmoothing(u, v);

                    if (smoothFactor > 0f)
                    {
                        ref Color vc = ref data.vertColor[i];
                        vc.r = Mathf.Lerp(vc.r, c1.r, smoothFactor);
                        vc.g = Mathf.Lerp(vc.g, c1.g, smoothFactor);
                        vc.b = Mathf.Lerp(vc.b, c1.b, smoothFactor);
                        _ = vc.a;
                    }
                }
            }
        }

        private readonly float GetHeightSmoothing(float u, float v)
        {
            float smoothU;
            float smoothV;

            if (u < smoothHeight)
                smoothU = u * smoothHR;
            else if (u > smoothH1M)
                smoothU = (1f - u) * smoothHR;
            else
                smoothU = 1f;

            if (v < smoothHeight)
                smoothV = v * smoothHR;
            else if (v > smoothH1M)
                smoothV = (1f - v) * smoothHR;
            else
                smoothV = 1f;

            return Mathf.Min(smoothU, smoothV);
        }

        private readonly float GetColorSmoothing(float u, float v)
        {
            float smoothU;
            float smoothV;

            if (u < smoothColor)
                smoothU = u * smoothCR;
            else if (u > smoothC1M)
                smoothU = (1f - u) * smoothCR;
            else
                smoothU = 1f;

            if (v < smoothColor)
                smoothV = v * smoothCR;
            else if (v > smoothC1M)
                smoothV = (1f - v) * smoothCR;
            else
                smoothV = 1f;

            return Mathf.Min(smoothU, smoothV);
        }
    }
}

// [BatchPQSMod(typeof(PQSMod_MapDecalTangent))]
public class MapDecalTangentShim(PQSMod_MapDecalTangent mod)
    : BatchPQSMod<PQSMod_MapDecalTangent>(mod)
{
    class State(PQSMod_MapDecalTangent mod) : BatchPQSModState
    {
        PQSMod_MapDecalTangent mod = mod;
        bool[] vertActive;

        public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
        {
            handle.Complete();
            vertActive = new bool[data.VertexCount];

            var vbData = PQS.vbData;
            vbData.buildQuad = data.buildQuad;
            vbData.gnomonicPlane = data.buildQuad.plane;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                mod.vertActive = false;

                data.CopyTo(vbData, i);
                mod.OnVertexBuildHeight(vbData);
                data.CopyFrom(vbData, i);

                vertActive[i] = mod.vertActive;
            }

            return handle;
        }

        public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
        {
            handle.Complete();

            var vbData = PQS.vbData;
            vbData.buildQuad = data.buildQuad;
            vbData.gnomonicPlane = data.buildQuad.plane;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                mod.vertActive = vertActive[i];

                data.CopyTo(vbData, i);
                mod.OnVertexBuildHeight(vbData);
                data.CopyFrom(vbData, i);
            }

            return handle;
        }

        public override void OnQuadBuilt(QuadBuildData data)
        {
            mod.OnQuadBuilt(data.buildQuad);
        }
    }

    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data)
    {
        mod.OnQuadPreBuild(data.buildQuad);
        return new State(mod);
    }
}
