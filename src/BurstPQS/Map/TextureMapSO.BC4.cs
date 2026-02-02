using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC4 compressed format. Encodes a single channel (R) in 8-byte blocks covering
    /// 4x4 pixels (4 bits/pixel). Each block stores two 8-bit endpoints and a 4x4 grid
    /// of 3-bit indices selecting from a 6- or 8-value palette interpolated between them.
    /// Uses the same alpha block encoding as DXT5.
    /// </summary>
    // [BurstCompile]
    public struct BC4 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public BC4(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.BC4);
            data = texture.GetRawTextureData<byte>();
            Width = texture.width;
            Height = texture.height;
            this.depth = depth;
        }

        readonly void GetComponents(int x, int y, out float r)
        {
            x = math.clamp(x, 0, Width - 1);
            y = math.clamp(y, 0, Height - 1);
            BlockCoords(x, y, Width, out int blockOffset, out int lx, out int ly, 8);
            r = DecodeBC4Block(data, blockOffset, lx, ly);
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            GetComponents(x, y, out float r);
            return DepthToFloat(r, 0f, 0f, 1f, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            GetComponents(x, y, out float r);
            return DepthToColor(r, 0f, 0f, 1f, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            GetComponents(x, y, out float r);
            return DepthToColor32(r, 0f, 0f, 1f, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            GetComponents(x, y, out float r);
            return DepthToHeightAlpha(r, 0f, 0f, 1f, depth);
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
