using System;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [BurstCompile]
    public struct RGBA32 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public RGBA32(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.RGBA32);
            this = new RGBA32(
                texture.GetRawTextureData<byte>(),
                texture.width,
                texture.height,
                depth
            );
        }

        public RGBA32(NativeArray<byte> data, int width, int height, MapSO.MapDepth depth)
        {
            int required = width * height * 4;
            if (data.Length < required)
                throw new ArgumentException(
                    $"Data length {data.Length} is too small for {width}x{height} RGBA32 texture (need at least {required})",
                    nameof(data)
                );
            this.data = data;
            Width = width;
            Height = height;
            this.depth = depth;
        }

        readonly void GetComponents(
            int x,
            int y,
            out float r,
            out float g,
            out float b,
            out float a
        )
        {
            int i = PixelIndex(x, y, Width, Height) * 4;
            r = data[i] * Byte2Float;
            g = data[i + 1] * Byte2Float;
            b = data[i + 2] * Byte2Float;
            a = data[i + 3] * Byte2Float;
        }

        readonly void GetByteComponents(
            int x,
            int y,
            out byte r,
            out byte g,
            out byte b,
            out byte a
        )
        {
            int i = PixelIndex(x, y, Width, Height) * 3;
            r = data[i];
            g = data[i + 1];
            b = data[i + 2];
            a = data[i + 3];
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b, out float a);
            return DepthToFloat(r, g, b, a, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b, out float a);
            return DepthToColor(r, g, b, a, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            GetByteComponents(x, y, out byte r, out byte g, out byte b, out byte a);
            return DepthToColor32(r, g, b, a, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b, out float a);
            return DepthToHeightAlpha(r, g, b, a, depth);
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
