using System;
using System.Runtime.CompilerServices;
using BurstPQS.Map;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_MapDecal))]
public class MapDecal(PQSMod_MapDecal mod) : BatchPQSMod<PQSMod_MapDecal>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        BurstMapSO? heightMap = null;
        if (mod.heightMap is not null)
            heightMap = BurstMapSO.Create(mod.heightMap);

        BurstMapSO? colorMap = null;
        if (mod.colorMap is not null)
            colorMap = BurstMapSO.Create(mod.colorMap);

        jobSet.Add(new BuildJob(mod) { heightMap = heightMap, colorMap = colorMap });
    }

    // [BurstCompile]
    struct BuildJob(PQSMod_MapDecal mod) : IBatchPQSHeightJob, IBatchPQSVertexJob, IDisposable
    {
        public double radius = mod.radius;
        public double heightMapDeformity = mod.heightMapDeformity;
        public bool cullBlack = mod.cullBlack;
        public bool useAlphaHeightSmoothing = mod.useAlphaHeightSmoothing;
        public bool absolute = mod.absolute;
        public float absoluteOffset = mod.absoluteOffset;
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

        public BurstMapSO? heightMap;
        public BurstMapSO? colorMap;
        public bool sphereIsBuildingMaps = mod.sphere.isBuildingMaps;

        public NativeArray<bool> vertActive;
        public NativeArray<bool> removeScatterFlags;

        public void BuildHeights(in BuildHeightsData data)
        {
            vertActive = new(data.VertexCount, Allocator.Temp);
            removeScatterFlags = new(data.VertexCount, Allocator.Temp);

            var sphere = data.sphere;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                if (sphereIsBuildingMaps)
                {
                    var quadAngle = Math.Acos(Vector3d.Dot(data.directionFromCenter[i], posNorm));
                    if (quadAngle > inclusionAngle)
                        continue;
                }

                var vertRot = rot * data.directionFromCenter[i];
                var u = (float)((vertRot.x * sphere.radius / radius + 1.0) * 0.5);
                var v = (float)((vertRot.z * sphere.radius / radius + 1.0) * 0.5);

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
                        sphere.radius + absoluteOffset + heightMapDeformity * ha.height,
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
            var sphere = data.sphere;

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
                var u = (float)((vertRot.x * sphere.radius / radius + 1.0) * 0.5);
                var v = (float)((vertRot.z * sphere.radius / radius + 1.0) * 0.5);

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
            heightMap?.Dispose();
            colorMap?.Dispose();

            heightMap = null;
            colorMap = null;
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
