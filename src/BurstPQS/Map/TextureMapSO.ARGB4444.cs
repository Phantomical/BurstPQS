using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// ARGB4444 format. Each pixel is a 16-bit value with 4 bits per channel
    /// in the order A, R, G, B (high to low bits).
    /// </summary>
    [BurstCompile]
    public struct ARGB4444 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        const float Nibble2Float = 1f / 15f;

        public ARGB4444(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.ARGB4444);
            data = texture.GetRawTextureData<byte>();
            Width = texture.width;
            Height = texture.height;
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
            int i = PixelIndex(x, y, Width, Height) * 2;
            int lo = data[i];
            int hi = data[i + 1];
            // Little-endian: lo = GB, hi = AR
            a = ((hi >> 4) & 0xF) * Nibble2Float;
            r = (hi & 0xF) * Nibble2Float;
            g = ((lo >> 4) & 0xF) * Nibble2Float;
            b = (lo & 0xF) * Nibble2Float;
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
