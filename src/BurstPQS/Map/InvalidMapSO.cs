using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Map;

/// <summary>
/// A no-op <see cref="IMapSO"/> that returns default values for all methods.
/// Used as a fallback when a valid map is not available.
/// </summary>
[BurstCompile]
public readonly struct InvalidMapSO : IMapSO
{
    public int Width => 1;
    public int Height => 1;

    public float GetPixelFloat(int x, int y) => 0f;

    public float GetPixelFloat(float x, float y) => 0f;

    public float GetPixelFloat(double x, double y) => 0f;

    public Color GetPixelColor(int x, int y) => Color.black;

    public Color GetPixelColor(float x, float y) => Color.black;

    public Color GetPixelColor(double x, double y) => Color.black;

    public Color32 GetPixelColor32(int x, int y) => new(0, 0, 0, 255);

    public Color32 GetPixelColor32(float x, float y) => new(0, 0, 0, 255);

    public Color32 GetPixelColor32(double x, double y) => new(0, 0, 0, 255);

    public HeightAlpha GetPixelHeightAlpha(int x, int y) => default;

    public HeightAlpha GetPixelHeightAlpha(float x, float y) => default;

    public HeightAlpha GetPixelHeightAlpha(double x, double y) => default;
}
