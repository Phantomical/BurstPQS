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
    /// BC5 compressed format. Encodes two channels (R, G) in 16-byte blocks covering
    /// 4x4 pixels (8 bits/pixel). Each block consists of two independent BC4 blocks
    /// side by side, one for the red channel and one for the green channel.
    /// </summary>
    [BurstCompile]
    internal struct BC5(CPUTexture2D.BC5 texture) : IMapSO
    {
        FormatMapSO<CPUTexture2D.BC5> mapSO = new(texture);

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
