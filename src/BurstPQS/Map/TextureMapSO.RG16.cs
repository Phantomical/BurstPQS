using System;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [BurstCompile]
    public struct RG16 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public RG16(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.RG16);
            this = new RG16(
                texture.GetRawTextureData<byte>(),
                texture.width,
                texture.height,
                depth
            );
        }

        public RG16(NativeArray<byte> data, int width, int height, MapSO.MapDepth depth)
        {
            int required = width * height * 2;
            if (data.Length < required)
                throw new ArgumentException(
                    $"Data length {data.Length} is too small for {width}x{height} RG16 texture (need at least {required})",
                    nameof(data)
                );
            this.data = data;
            Width = width;
            Height = height;
            this.depth = depth;
        }

        readonly void GetComponents(int x, int y, out float r, out float g)
        {
            int i = PixelIndex(x, y, Width, Height) * 2;
            r = data[i] * Byte2Float;
            g = data[i + 1] * Byte2Float;
        }

        readonly void GetByteComponents(int x, int y, out byte r, out byte g)
        {
            int i = PixelIndex(x, y, Width, Height) * 2;
            r = data[i];
            g = data[i + 1];
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            GetComponents(x, y, out float r, out float g);
            return DepthToFloat(r, g, 1f, 1f, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            GetComponents(x, y, out float r, out float g);
            return DepthToColor(r, g, 1f, 1f, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            GetByteComponents(x, y, out byte r, out byte g);
            return DepthToColor32(r, g, 255, 255, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            GetComponents(x, y, out float r, out float g);
            return DepthToHeightAlpha(r, g, 1f, 1f, depth);
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
