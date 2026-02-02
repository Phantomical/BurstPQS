using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// DXT1/BC1 compressed format. Encodes RGB with optional 1-bit alpha in 8-byte blocks
    /// covering 4x4 pixels (4 bits/pixel). Each block stores two RGB565 color endpoints and
    /// a 4x4 grid of 2-bit indices selecting from a 4-color palette interpolated between them.
    /// When <c>color0 &lt;= color1</c>, index 3 produces transparent black (1-bit alpha).
    /// </summary>
    // [BurstCompile]
    public struct DXT1 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public DXT1(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.DXT1);
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
            x = math.clamp(x, 0, Width - 1);
            y = math.clamp(y, 0, Height - 1);
            BlockCoords(x, y, Width, out int blockOffset, out int lx, out int ly, 8);
            DecodeDXT1Pixel(data, blockOffset, lx, ly, out r, out g, out b, out a);
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
