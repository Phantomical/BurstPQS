using UnityEngine;

namespace BurstPQS.Util;

public interface IBurstMapSO
{
    public Color GetPixelColor(float x, float y);
    public Color GetPixelColor(double x, double y);
    public Color32 GetPixelColor32(float x, float y);
    public Color32 GetPixelColor32(double x, double y);
    public float GetPixelFloat(float x, float y);
    public float GetPixelFloat(double x, double y);
    public BurstMapSO.HeightAlpha GetPixelHeightAlpha(float x, float y);
    public BurstMapSO.HeightAlpha GetPixelHeightAlpha(double x, double y);
}
