using System;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [BurstCompile]
    public struct Alpha8 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public Alpha8(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.Alpha8);
            this = new Alpha8(
                texture.GetRawTextureData<byte>(),
                texture.width,
                texture.height,
                depth
            );
        }

        public Alpha8(NativeArray<byte> data, int width, int height, MapSO.MapDepth depth)
        {
            int required = width * height;
            if (data.Length < required)
                throw new ArgumentException(
                    $"Data length {data.Length} is too small for {width}x{height} Alpha8 texture (need at least {required})",
                    nameof(data)
                );
            this.data = data;
            Width = width;
            Height = height;
            this.depth = depth;
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            float a = data[PixelIndex(x, y, Width, Height)] * Byte2Float;

            if (depth == MapSO.MapDepth.Greyscale)
                return DepthToFloat(a, 1f, 1f, 1f, depth);
            else
                return DepthToFloat(1f, 1f, 1f, a, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            float a = data[PixelIndex(x, y, Width, Height)] * Byte2Float;

            if (depth == MapSO.MapDepth.Greyscale)
                return DepthToColor(a, 1f, 1f, 1f, depth);
            else
                return DepthToColor(1f, 1f, 1f, a, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            byte a = data[PixelIndex(x, y, Width, Height)];

            if (depth == MapSO.MapDepth.Greyscale)
                return DepthToColor32(a, 255, 255, 255, depth);
            else
                return DepthToColor32(255, 255, 255, a, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            float a = data[PixelIndex(x, y, Width, Height)] * Byte2Float;
            if (depth == MapSO.MapDepth.Greyscale)
                return DepthToHeightAlpha(a, 1f, 1f, 1f, depth);
            else
                return DepthToHeightAlpha(1f, 1f, 1f, a, depth);
        }

        public float GetPixelFloat(float x, float y) => MapSODefaults.GetPixelFloat(ref this, x, y);

        public float GetPixelFloat(double x, double y) =>
            MapSODefaults.GetPixelFloat(ref this, x, y);

        public Color GetPixelColor(float x, float y) => MapSODefaults.GetPixelColor(ref this, x, y);

        public Color GetPixelColor(double x, double y) =>
            MapSODefaults.GetPixelColor(ref this, x, y);

        public Color32 GetPixelColor32(float x, float y) =>
            MapSODefaults.GetPixelColor32(ref this, x, y);

        public Color32 GetPixelColor32(double x, double y) =>
            MapSODefaults.GetPixelColor32(ref this, x, y);

        public HeightAlpha GetPixelHeightAlpha(float x, float y) =>
            MapSODefaults.GetPixelHeightAlpha(ref this, x, y);

        public HeightAlpha GetPixelHeightAlpha(double x, double y) =>
            MapSODefaults.GetPixelHeightAlpha(ref this, x, y);
    }
}
