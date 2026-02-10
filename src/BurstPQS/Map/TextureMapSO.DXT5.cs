using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// DXT5/BC3 compressed format. Encodes RGBA in 16-byte blocks covering 4x4 pixels
    /// (8 bits/pixel). Each block consists of an 8-byte alpha block followed by an 8-byte
    /// DXT1 color block. The alpha block stores two 8-bit endpoints and a 4x4 grid of
    /// 3-bit indices selecting from an 8-value palette interpolated between them.
    /// </summary>
    [BurstCompile]
    public struct DXT5 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public DXT5(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.DXT5);
            this = new DXT5(
                texture.GetRawTextureData<byte>(),
                texture.width,
                texture.height,
                depth
            );
        }

        public DXT5(NativeArray<byte> data, int width, int height, MapSO.MapDepth depth)
        {
            int required = ((width + 3) / 4) * ((height + 3) / 4) * 16;
            if (data.Length < required)
                throw new ArgumentException(
                    $"Data length {data.Length} is too small for {width}x{height} DXT5 texture (need at least {required})",
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
            x = math.clamp(x, 0, Width - 1);
            y = math.clamp(y, 0, Height - 1);
            BlockCoords(x, y, Width, out int blockOffset, out int lx, out int ly, 16);
            a = DecodeBC4Block(data, blockOffset, lx, ly);
            DecodeDXT1Pixel(data, blockOffset + 8, lx, ly, out r, out g, out b, out _);
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
            GetComponents(x, y, out float r, out float g, out float b, out float a);
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
