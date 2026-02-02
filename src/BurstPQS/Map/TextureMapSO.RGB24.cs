using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    [BurstCompile]
    public struct RGB24 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public RGB24(Texture2D texture, MapSO.MapDepth depth)
        {
            data = texture.GetRawTextureData<byte>();
            Width = texture.width;
            Height = texture.height;
            this.depth = depth;
        }

        readonly void GetComponents(int x, int y, out float r, out float g, out float b)
        {
            int i = PixelIndex(x, y, Width, Height) * 3;
            r = data[i] * Byte2Float;
            g = data[i + 1] * Byte2Float;
            b = data[i + 2] * Byte2Float;
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToFloat(r, g, b, 1f, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToColor(r, g, b, 1f, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToColor32(r, g, b, 1f, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToHeightAlpha(r, g, b, 1f, depth);
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
