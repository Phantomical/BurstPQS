using System;
using System.Runtime.CompilerServices;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class MapDecal : BatchPQSMod<PQSMod_MapDecal>
{
    bool[] vertActive;

    public MapDecal(PQSMod_MapDecal mod)
        : base(mod) { }

    public override unsafe void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        if (!mod.quadActive && mod.buildHeight)
            return;

        if (vertActive is null || vertActive.Length != data.VertexCount)
            vertActive = new bool[data.VertexCount];

        var info = new BurstInfo(mod);
        BurstMapSO.Guard? guard = null;
        BurstMapSO? heightMap = null;

        if (mod.heightMap is not null)
        {
            guard = BurstMapSO.Create(mod.heightMap, out var map);
            heightMap = map;
        }

        try
        {
            fixed (bool* pVertActive = vertActive)
            {
                BuildHeights(
                    in data.burstData,
                    in info,
                    heightMap,
                    mod.sphere.isBuildingMaps,
                    mod.sphere.radius,
                    new(pVertActive, vertActive.Length)
                );
            }
        }
        finally
        {
            guard?.Dispose();
        }
    }

    public override unsafe void OnBatchVertexBuild(in QuadBuildData data)
    {
        if (!mod.quadActive && mod.buildHeight)
            return;
        if (vertActive is null)
            return;

        var info = new BurstInfo(mod);
        BurstMapSO.Guard? guard = null;
        BurstMapSO? colorMap = null;

        if (mod.colorMap is not null)
        {
            guard = BurstMapSO.Create(mod.colorMap, out var map);
            colorMap = map;
        }

        try
        {
            fixed (bool* pVertActive = vertActive)
            {
                BuildVerts(
                    in data.burstData,
                    in info,
                    colorMap,
                    mod.sphere.radius,
                    new(pVertActive, vertActive.Length)
                );
            }
        }
        finally
        {
            guard?.Dispose();
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstInfo info,
        [NoAlias] in NullableWrap<BurstMapSO> heightMap,
        bool sphereIsBuildingMaps,
        double sphereRadius,
        [NoAlias] in MemorySpan<bool> vertActive
    )
    {
        info.BuildHeights(in data, heightMap, sphereIsBuildingMaps, sphereRadius, vertActive);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVerts(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstInfo info,
        [NoAlias] in NullableWrap<BurstMapSO> colorMap,
        double sphereRadius,
        [NoAlias] in MemorySpan<bool> vertActive
    )
    {
        info.BuildVerts(in data, colorMap, vertActive, sphereRadius);
    }

    struct BurstInfo(PQSMod_MapDecal mod)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void BuildHeights(
            in BurstQuadBuildData data,
            BurstMapSO? nHeightMap,
            bool sphereIsBuildingMaps,
            double sphereRadius,
            MemorySpan<bool> vertActive
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void BuildVerts(
            in BurstQuadBuildData data,
            BurstMapSO? nColorMap,
            MemorySpan<bool> vertActive,
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
