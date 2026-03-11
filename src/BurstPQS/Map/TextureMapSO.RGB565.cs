using System;
using KSPTextureLoader;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// RGB565 format. Each pixel is a 16-bit value with 5 bits red, 6 bits green,
    /// 5 bits blue (high to low bits). No alpha channel.
    /// </summary>
    [BurstCompile]
    internal struct RGB565(CPUTexture2D.RGB565 texture) : IMapSO
    {
        FormatMapSO<CPUTexture2D.RGB565> mapSO = new(texture);

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
