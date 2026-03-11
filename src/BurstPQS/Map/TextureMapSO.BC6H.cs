using System;
using KSPTextureLoader;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC6H compressed format. Encodes HDR RGB (no alpha) in 16-byte blocks covering
    /// 4x4 pixels (8 bits/pixel). Supports 14 modes (0-13) with 1 or 2 subsets,
    /// half-float endpoints (10-16 bits) with optional delta encoding, and 3- or 4-bit
    /// indices. Endpoints are unquantized to 16-bit half-precision floats and interpolated.
    /// Comes in signed (<c>BC6H_SF16</c>) and unsigned (<c>BC6H_UF16</c>) variants.
    /// </summary>
    [BurstCompile]
    internal struct BC6H(CPUTexture2D.BC6H texture) : IMapSO
    {
        FormatMapSO<CPUTexture2D.BC6H> mapSO = new(texture);

        public readonly int Width => mapSO.Width;
        public readonly int Height => mapSO.Height;
        public readonly MapSO.MapDepth Depth => mapSO.Depth;

        public float GetPixelFloat(int x, int y) => mapSO.GetPixelFloat(x, y);

        public float GetPixelFloat(float x, float y) => mapSO.GetPixelFloat(x, y);

        public float GetPixelFloat(double x, double y) => mapSO.GetPixelFloat(x, y);

        public Color GetPixelColor(int x, int y) => mapSO.GetPixelColor(x, y);

        public Color GetPixelColor(float x, float y) => mapSO.GetPixelColor(x, y);

        public Color GetPixelColor(double x, double y) => mapSO.GetPixelColor(x, y);

        public Color32 GetPixelColor32(int x, int y) => mapSO.GetPixelColor32(x, y);

        public Color32 GetPixelColor32(float x, float y) => mapSO.GetPixelColor32(x, y);

        public Color32 GetPixelColor32(double x, double y) => mapSO.GetPixelColor32(x, y);

        public HeightAlpha GetPixelHeightAlpha(int x, int y) => mapSO.GetPixelHeightAlpha(x, y);

        public HeightAlpha GetPixelHeightAlpha(float x, float y) => mapSO.GetPixelHeightAlpha(x, y);

        public HeightAlpha GetPixelHeightAlpha(double x, double y) =>
            mapSO.GetPixelHeightAlpha(x, y);
    }
}
