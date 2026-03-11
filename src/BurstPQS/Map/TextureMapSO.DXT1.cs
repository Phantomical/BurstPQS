using KSPTextureLoader;
using Unity.Burst;
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
    [BurstCompile]
    internal struct DXT1(CPUTexture2D.DXT1 texture) : IMapSO
    {
        FormatMapSO<CPUTexture2D.DXT1> mapSO = new(texture);

        public readonly int Width => mapSO.Width;
        public readonly int Height => mapSO.Height;
        public readonly MapSO.MapDepth Depth => mapSO.Depth;

        public float GetPixelFloat(int x, int y) => mapSO.GetPixelFloat(x, y);

        public float GetPixelFloat(float x, float y) => mapSO.GetPixelFloat(x, y);

        public float GetPixelFloat(double x, double y) => mapSO.GetPixelFloat(x, y);

        public Color GetPixelColor(int x, int y) => mapSO.GetPixelColor(x, y);

        public Color GetPixelColor(float x, float y) => mapSO.GetPixelColor(x, y);

        public Color GetPixelColor(double x, double y) => mapSO.GetPixelColor(x, y);

        public Color32 GetPixelColor32(int x, int y) => mapSO.GetPixelColor32(x, y);

        public Color32 GetPixelColor32(float x, float y) => mapSO.GetPixelColor32(x, y);

        public Color32 GetPixelColor32(double x, double y) => mapSO.GetPixelColor32(x, y);

        public HeightAlpha GetPixelHeightAlpha(int x, int y) => mapSO.GetPixelHeightAlpha(x, y);

        public HeightAlpha GetPixelHeightAlpha(float x, float y) => mapSO.GetPixelHeightAlpha(x, y);

        public HeightAlpha GetPixelHeightAlpha(double x, double y) =>
            mapSO.GetPixelHeightAlpha(x, y);
    }
}
