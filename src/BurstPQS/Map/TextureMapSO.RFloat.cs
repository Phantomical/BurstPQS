using System;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [BurstCompile]
    public struct RFloat : IMapSO
    {
        NativeArray<float> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public RFloat(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.RFloat);
            this = new RFloat(
                texture.GetRawTextureData<float>(),
                texture.width,
                texture.height,
                depth
            );
        }

        public RFloat(NativeArray<float> data, int width, int height, MapSO.MapDepth depth)
        {
            int required = width * height;
            if (data.Length < required)
                throw new ArgumentException(
                    $"Data length {data.Length} is too small for {width}x{height} RFloat texture (need at least {required})",
                    nameof(data)
                );
            this.data = data;
            Width = width;
            Height = height;
            this.depth = depth;
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            float v = data[PixelIndex(x, y, Width, Height)];
            return DepthToFloat(v, 1f, 1f, 1f, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            float v = data[PixelIndex(x, y, Width, Height)];
            return DepthToColor(v, 1f, 1f, 1f, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            float v = data[PixelIndex(x, y, Width, Height)];
            return DepthToColor32(v, 1f, 1f, 1f, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            float v = data[PixelIndex(x, y, Width, Height)];
            return DepthToHeightAlpha(v, 1f, 1f, 1f, depth);
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
