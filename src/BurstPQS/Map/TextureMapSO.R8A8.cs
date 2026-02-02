using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// R8A8 format (not a native Unity format). Each pixel is 2 bytes: R, A.
    /// Used by some KSP mods.
    /// </summary>
    [BurstCompile]
    public struct R8A8 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public R8A8(Texture2D texture, MapSO.MapDepth depth)
        {
            data = texture.GetRawTextureData<byte>();
            Width = texture.width;
            Height = texture.height;
            this.depth = depth;
        }

        readonly void GetComponents(int x, int y, out float r, out float a)
        {
            int i = PixelIndex(x, y, Width, Height) * 2;
            r = data[i] * Byte2Float;
            a = data[i + 1] * Byte2Float;
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            GetComponents(x, y, out float r, out float a);
            return DepthToFloat(r, 0f, 0f, a, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            GetComponents(x, y, out float r, out float a);
            return DepthToColor(r, 0f, 0f, a, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            GetComponents(x, y, out float r, out float a);
            return DepthToColor32(r, 0f, 0f, a, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            GetComponents(x, y, out float r, out float a);
            return DepthToHeightAlpha(r, 0f, 0f, a, depth);
        }

        public float GetPixelFloat(float x, float y) => MapSODefaults.GetPixelFloat(ref this, x, y);

        public float GetPixelFloat(double x, double y) =>
            MapSODefaults.GetPixelFloat(ref this, x, y);

        public Color GetPixelColor(float x, float y) =>
            MapSODefaults.GetPixelColor(ref this, x, y);

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
