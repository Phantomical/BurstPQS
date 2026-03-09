using BurstPQS.Map;
using KSPTextureLoader;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Kopernicus.Map;

static partial class BurstKopernicusMapSO
{
    [BurstCompile]
    public struct RGBA4444(CPUTexture2D.RGBA4444 texture, MapSO.MapDepth depth) : IMapSO
    {
        CPUTexture2D.RGBA4444 texture = texture;
        MapSO.MapDepth depth = depth;

        public int Width => texture.Width;
        public int Height => texture.Height;

        public float GetPixelFloat(int x, int y) =>
            BurstKopernicusMapSO.GetPixelFloat(ref texture, depth, x, y);

        public Color GetPixelColor(int x, int y) =>
            BurstKopernicusMapSO.GetPixelColor(ref texture, depth, x, y);

        public Color32 GetPixelColor32(int x, int y) =>
            BurstKopernicusMapSO.GetPixelColor32(ref texture, depth, x, y);

        public HeightAlpha GetPixelHeightAlpha(int x, int y) =>
            BurstKopernicusMapSO.GetPixelHeightAlpha(ref texture, depth, x, y);

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
