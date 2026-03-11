using System;
using BurstPQS.Map;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_TextureAtlas))]
public class TextureAtlas(PQSMod_TextureAtlas mod) : BatchPQSMod<PQSMod_TextureAtlas>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        if (!mod.CanTextureAtlasModBeUsed())
            return;

        jobSet.Add(
            new BuildJob
            {
                textureAtlasMap = BurstMapSO.Create(mod.textureAtlasMap),
                mod = new(mod),
            }
        );
    }

    public override void OnQuadBuilt(PQ quad) { }

    [BurstCompile]
    struct BuildJob : IBatchPQSMeshJob, IBatchPQSMeshBuiltJob, IDisposable
    {
        public BurstMapSO textureAtlasMap;
        public ObjectHandle<PQSMod_TextureAtlas> mod;

        int materialBlendCount;

        public void BuildMesh(in BuildMeshData data)
        {
            int mapWidth = textureAtlasMap.Width;
            int mapHeight = textureAtlasMap.Height;

            // Phase 1: Accumulate total strength per quantized index across all
            // vertices. Indices are byte values quantized to multiples of 10.
            var indexTotals = new NativeArray<float>(
                256,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory
            );

            for (int i = 0; i < data.VertexCount; ++i)
            {
                GetTexCoords(data.directionFromCenter[i], out double texX, out double texY);
                ConstructBilinearCoords(
                    texX,
                    texY,
                    mapWidth,
                    mapHeight,
                    out int minX,
                    out int maxX,
                    out int minY,
                    out int maxY,
                    out float centerX,
                    out float centerY
                );

                AccumulateCorner(
                    indexTotals,
                    textureAtlasMap.GetPixelColor32(minX, minY),
                    centerX,
                    centerY,
                    minX,
                    minY
                );
                AccumulateCorner(
                    indexTotals,
                    textureAtlasMap.GetPixelColor32(maxX, minY),
                    centerX,
                    centerY,
                    maxX,
                    minY
                );
                AccumulateCorner(
                    indexTotals,
                    textureAtlasMap.GetPixelColor32(minX, maxY),
                    centerX,
                    centerY,
                    minX,
                    maxY
                );
                AccumulateCorner(
                    indexTotals,
                    textureAtlasMap.GetPixelColor32(maxX, maxY),
                    centerX,
                    centerY,
                    maxX,
                    maxY
                );
            }

            // Phase 2: Find top NUM_PACKED indices by total strength.
            int top0 = 255,
                top1 = 255,
                top2 = 255,
                top3 = 255;
            float str0 = 0f,
                str1 = 0f,
                str2 = 0f,
                str3 = 0f;

            for (int idx = 0; idx < 256; idx++)
            {
                float total = indexTotals[idx];
                if (total <= 0f)
                    continue;

                if (total > str0)
                {
                    top3 = top2;
                    str3 = str2;
                    top2 = top1;
                    str2 = str1;
                    top1 = top0;
                    str1 = str0;
                    top0 = idx;
                    str0 = total;
                }
                else if (total > str1)
                {
                    top3 = top2;
                    str3 = str2;
                    top2 = top1;
                    str2 = str1;
                    top1 = idx;
                    str1 = total;
                }
                else if (total > str2)
                {
                    top3 = top2;
                    str3 = str2;
                    top2 = idx;
                    str2 = total;
                }
                else if (total > str3)
                {
                    top3 = idx;
                    str3 = total;
                }
            }

            if (str1 == 0f)
                materialBlendCount = 1;
            else if (str2 == 0f)
                materialBlendCount = 2;
            else if (str3 == 0f)
                materialBlendCount = 3;
            else
                materialBlendCount = 4;

            // Encode UV3.x: pack top 4 indices into a single float.
            float packedIndices =
                top0 / 10f * 32768f + top1 / 10f * 1024f + top2 / 10f * 32f + top3 / 10f;

            // Phase 3: Compute per-vertex weights for top 4 and pack UV3.
            for (int i = 0; i < data.VertexCount; ++i)
            {
                GetTexCoords(data.directionFromCenter[i], out double texX, out double texY);
                ConstructBilinearCoords(
                    texX,
                    texY,
                    mapWidth,
                    mapHeight,
                    out int minX,
                    out int maxX,
                    out int minY,
                    out int maxY,
                    out float centerX,
                    out float centerY
                );

                float w0 = 0f,
                    w1 = 0f,
                    w2 = 0f,
                    w3 = 0f;

                AccumulateVertexWeights(
                    textureAtlasMap.GetPixelColor32(minX, minY),
                    centerX,
                    centerY,
                    minX,
                    minY,
                    top0,
                    top1,
                    top2,
                    top3,
                    ref w0,
                    ref w1,
                    ref w2,
                    ref w3
                );
                AccumulateVertexWeights(
                    textureAtlasMap.GetPixelColor32(maxX, minY),
                    centerX,
                    centerY,
                    maxX,
                    minY,
                    top0,
                    top1,
                    top2,
                    top3,
                    ref w0,
                    ref w1,
                    ref w2,
                    ref w3
                );
                AccumulateVertexWeights(
                    textureAtlasMap.GetPixelColor32(minX, maxY),
                    centerX,
                    centerY,
                    minX,
                    maxY,
                    top0,
                    top1,
                    top2,
                    top3,
                    ref w0,
                    ref w1,
                    ref w2,
                    ref w3
                );
                AccumulateVertexWeights(
                    textureAtlasMap.GetPixelColor32(maxX, maxY),
                    centerX,
                    centerY,
                    maxX,
                    maxY,
                    top0,
                    top1,
                    top2,
                    top3,
                    ref w0,
                    ref w1,
                    ref w2,
                    ref w3
                );

                // Normalize weights to sum to 1.
                float wTotal = w0 + w1 + w2 + w3;
                if (wTotal != 0f)
                {
                    w0 /= wTotal;
                    w1 /= wTotal;
                    w2 /= wTotal;
                }
                else
                {
                    w0 = 1f;
                }

                // Encode UV3.y: pack 3 weights (4th is implied as 1 - sum).
                float packedWeights =
                    math.floor(w0 * 200f) * 200f * 200f + math.floor(w1 * 100f) * 200f + w2 * 100f;

                data.uv3s[i] = new Vector2(packedIndices, packedWeights);
            }
        }

        /// <summary>
        /// Compute texture atlas UV coordinates from a vertex direction vector.
        /// Replicates the coordinate transform in
        /// <c>CBTextureAtlasSO.GetCBTextureAtlasPoint</c>.
        /// </summary>
        static void GetTexCoords(Vector3d direction, out double texX, out double texY)
        {
            double lat = math.asin(MathUtil.Clamp(direction.y, -1.0, 1.0));

            double dx = direction.x;
            double dz = direction.z;
            double sqrMag = dx * dx + dz * dz;

            double lon;
            if (sqrMag == 0.0)
                lon = 0.0;
            else if (dx < 0.0)
                lon = Math.PI - math.asin(dz / math.sqrt(sqrMag));
            else
                lon = math.asin(dz / math.sqrt(sqrMag));

            // CBTextureAtlasSO.GetCBTextureAtlasPoint coordinate transform
            const double HALF_PI = Math.PI * 0.5;
            const double TWO_PI = Math.PI * 2.0;

            lon -= HALF_PI;
            lon -= math.floor(lon / TWO_PI) * TWO_PI;

            texY = lat / Math.PI + 0.5;
            texX = 1.0 - lon / TWO_PI;
        }

        /// <summary>
        /// Replicate <c>MapSO.ConstructBilinearCoords</c> (double overload).
        /// </summary>
        static void ConstructBilinearCoords(
            double x,
            double y,
            int width,
            int height,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY,
            out float centerX,
            out float centerY
        )
        {
            x = math.abs(x - math.floor(x));
            y = math.abs(y - math.floor(y));

            double cx = x * width;
            double cy = y * height;
            centerX = (float)cx;
            centerY = (float)cy;

            minX = (int)math.floor(cx);
            maxX = (int)math.ceil(cx);
            minY = (int)math.floor(cy);
            maxY = (int)math.ceil(cy);

            if (maxX == width)
                maxX = 0;
            if (maxY == height)
                maxY = 0;
        }

        /// <summary>
        /// Accumulate a corner pixel's contribution to global per-index totals.
        /// </summary>
        static void AccumulateCorner(
            NativeArray<float> indexTotals,
            Color32 color,
            float centerX,
            float centerY,
            int cornerX,
            int cornerY
        )
        {
            float wx = 1f - math.abs(centerX - cornerX);
            float wy = 1f - math.abs(centerY - cornerY);
            float bilinearWeight = wx * wy;

            // Quantize R and B to multiples of 10, matching stock byte truncation.
            int rIdx = (byte)((int)math.round(color.r / 10f) * 10);
            int bIdx = (byte)((int)math.round(color.b / 10f) * 10);

            indexTotals[rIdx] += color.g * bilinearWeight;
            indexTotals[bIdx] += (255f - color.g) * bilinearWeight;
        }

        /// <summary>
        /// Accumulate a corner pixel's contribution to per-vertex weights for the
        /// top 4 indices.
        /// </summary>
        static void AccumulateVertexWeights(
            Color32 color,
            float centerX,
            float centerY,
            int cornerX,
            int cornerY,
            int top0,
            int top1,
            int top2,
            int top3,
            ref float w0,
            ref float w1,
            ref float w2,
            ref float w3
        )
        {
            float wx = 1f - math.abs(centerX - cornerX);
            float wy = 1f - math.abs(centerY - cornerY);
            float bilinearWeight = wx * wy;

            int rIdx = (byte)((int)math.round(color.r / 10f) * 10);
            int bIdx = (byte)((int)math.round(color.b / 10f) * 10);

            float rStr = color.g * bilinearWeight;
            float bStr = (255f - color.g) * bilinearWeight;

            if (rIdx == top0)
                w0 += rStr;
            else if (rIdx == top1)
                w1 += rStr;
            else if (rIdx == top2)
                w2 += rStr;
            else if (rIdx == top3)
                w3 += rStr;

            if (bIdx == top0)
                w0 += bStr;
            else if (bIdx == top1)
                w1 += bStr;
            else if (bIdx == top2)
                w2 += bStr;
            else if (bIdx == top3)
                w3 += bStr;
        }

        public void OnMeshBuilt(PQ quad)
        {
            var m = mod.Target;

            Material material = materialBlendCount switch
            {
                1 => m.material1Blend,
                2 => m.material2Blend,
                3 => m.material3Blend,
                _ => m.material4Blend,
            };

            if (quad.sphereRoot.useSharedMaterial)
                quad.meshRenderer.sharedMaterial = material;
            else
                quad.meshRenderer.material = material;
        }

        public void Dispose()
        {
            textureAtlasMap.Dispose();
            mod.Dispose();
        }
    }
}
