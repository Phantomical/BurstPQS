using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    // [BurstCompile]
    public struct R8 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public R8(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.R8);
            data = texture.GetRawTextureData<byte>();
            Width = texture.width;
            Height = texture.height;
            this.depth = depth;
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            float v = data[PixelIndex(x, y, Width, Height)] * Byte2Float;
            return DepthToFloat(v, 0f, 0f, 1f, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            float v = data[PixelIndex(x, y, Width, Height)] * Byte2Float;
            return DepthToColor(v, 0f, 0f, 1f, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            byte v = data[PixelIndex(x, y, Width, Height)];
            return DepthToColor32(v, 0, 0, 255, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            float v = data[PixelIndex(x, y, Width, Height)] * Byte2Float;
            return DepthToHeightAlpha(v, 0f, 0f, 1f, depth);
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
