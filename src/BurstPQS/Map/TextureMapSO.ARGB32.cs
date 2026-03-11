using System;
using KSPTextureLoader;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [BurstCompile]
    internal struct ARGB32(CPUTexture2D.ARGB32 texture) : IMapSO
    {
        FormatMapSO<CPUTexture2D.ARGB32> mapSO = new(texture);

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
