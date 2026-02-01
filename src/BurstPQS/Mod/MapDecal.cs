using System;
using System.Runtime.CompilerServices;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_MapDecal))]
public class MapDecal(PQSMod_MapDecal mod) : BatchPQSMod<PQSMod_MapDecal>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        BurstMapSO? heightMap = null;
        if (mod.heightMap is not null)
            heightMap = new BurstMapSO(mod.heightMap);

        BurstMapSO? colorMap = null;
        if (mod.colorMap is not null)
            colorMap = new BurstMapSO(mod.colorMap);

        jobSet.Add(new BuildJob
        {
            radius = mod.radius,
            heightMapDeformity = mod.heightMapDeformity,
            cullBlack = mod.cullBlack,
            useAlphaHeightSmoothing = mod.useAlphaHeightSmoothing,
            absolute = mod.absolute,
            absoluteOffset = mod.absoluteOffset,
            smoothHeight = mod.smoothHeight,
            smoothColor = mod.smoothColor,
            removeScatter = mod.removeScatter,
            DEBUG_HighlightInclusion = mod.DEBUG_HighlightInclusion,
            inclusionAngle = mod.inclusionAngle,
            quadActive = mod.quadActive,
            posNorm = mod.posNorm,
            rot = mod.rot,
            buildHeight = mod.buildHeight,
            smoothCR = mod.smoothCR,
            smoothC1M = mod.smoothC1M,
            smoothHR = mod.smoothHR,
            smoothH1M = mod.smoothH1M,
            heightMap = heightMap,
            colorMap = colorMap,
            sphereIsBuildingMaps = mod.sphere.isBuildingMaps,
            sphereRadius = mod.sphere.radius,
            vertActive = null,
            removeScatterFlags = null,
        });
    }

    [BurstCompile]
    unsafe struct BuildJob : IBatchPQSHeightJob, IBatchPQSVertexJob, IDisposable
    {
        public double radius;
        public double heightMapDeformity;
        public bool cullBlack;
        public bool useAlphaHeightSmoothing;
        public bool absolute;
        public float absoluteOffset;
        public float smoothHeight;
        public float smoothColor;
        public bool removeScatter;
        public bool DEBUG_HighlightInclusion;
        public double inclusionAngle;
        public bool quadActive;
        public Vector3d posNorm;
        public Quaternion rot;
        public bool buildHeight;
        public float smoothCR;
        public float smoothC1M;
        public float smoothHR;
        public float smoothH1M;

        public BurstMapSO? heightMap;
        public BurstMapSO? colorMap;
        public bool sphereIsBuildingMaps;
        public double sphereRadius;

        public bool* vertActive;
        public bool* removeScatterFlags;

        public void BuildHeights(in BuildHeightsData data)
        {
            int vertexCount = data.VertexCount;

            vertActive = (bool*)UnsafeUtility.Malloc(
                vertexCount * sizeof(bool),
                UnsafeUtility.AlignOf<bool>(),
                Unity.Collections.Allocator.Temp
            );
            UnsafeUtility.MemClear(vertActive, vertexCount * sizeof(bool));

            removeScatterFlags = (bool*)UnsafeUtility.Malloc(
                vertexCount * sizeof(bool),
                UnsafeUtility.AlignOf<bool>(),
                Unity.Collections.Allocator.Temp
            );
            UnsafeUtility.MemClear(removeScatterFlags, vertexCount * sizeof(bool));

            for (int i = 0; i < vertexCount; ++i)
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
                if (heightMap is not BurstMapSO hMap)
                    return;

                var ha = hMap.GetPixelHeightAlpha(u, v);
                var smoothFactor = GetHeightSmoothing(u, v);
                if (useAlphaHeightSmoothing)
                    smoothFactor *= ha.alpha;
                if (!(smoothFactor > 0f))
                    continue;
                if (removeScatter)
                    removeScatterFlags[i] = true;

                if (cullBlack && ha.height <= 0f)
                {
                    // do nothing
                }
                else if (absolute)
                {
                    data.vertHeight[i] = MathUtil.Lerp(
                        data.vertHeight[i],
                        sphereRadius + absoluteOffset + heightMapDeformity * ha.height,
                        smoothFactor
                    );
                }
                else
                {
                    data.vertHeight[i] = MathUtil.Lerp(
                        data.vertHeight[i],
                        data.vertHeight[i] + heightMapDeformity * ha.height,
                        smoothFactor
                    );
                }
            }
        }

        public void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                if (removeScatterFlags[i])
                    data.allowScatter[i] = false;

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
                else if (colorMap is BurstMapSO cMap)
                {
                    var c1 = cMap.GetPixelColor(u, v);
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

        public void Dispose()
        {
            if (vertActive != null)
            {
                UnsafeUtility.Free(vertActive, Unity.Collections.Allocator.Temp);
                vertActive = null;
            }
            if (removeScatterFlags != null)
            {
                UnsafeUtility.Free(removeScatterFlags, Unity.Collections.Allocator.Temp);
                removeScatterFlags = null;
            }
            heightMap?.Dispose();
            colorMap?.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
