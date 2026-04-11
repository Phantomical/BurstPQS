using System;
using BurstPQS.Map;
using KSP.Testing;
using KSPTextureLoader;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Test.Map;

/// <summary>
/// Tests that TextureMapSO pixel accessors return correct raw values for an RGBA32 texture.
/// TextureMapSO matches Unity Texture2D behavior (no stock MapSO depth interpretation).
/// </summary>
public class DepthInterpretationTests : BurstPQSTestBase
{
    // Known test pixel: R=100, G=150, B=200, A=50
    const byte R = 100;
    const byte G = 150;
    const byte B = 200;
    const byte A = 50;
    const float Rf = R / 255f;
    const float Gf = G / 255f;
    const float Bf = B / 255f;
    const float Af = A / 255f;

    // Unity's Color.grayscale = 0.299*r + 0.587*g + 0.114*b
    static readonly float Grayscale = 0.299f * Rf + 0.587f * Gf + 0.114f * Bf;

    static (TextureMapSO.RGBA32 map, NativeArray<byte> data) MakeSinglePixelRGBA32()
    {
        var bytes = new byte[] { R, G, B, A };
        var data = new NativeArray<byte>(bytes, Allocator.Persistent);
        return (
            new TextureMapSO.RGBA32(new CPUTexture2D.RGBA32(data, 1, 1, 1), TextureWrapMode.Repeat),
            data
        );
    }

    [TestInfo("RGBA32_Float")]
    public void TestFloat()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            assertFloatEquals("RGBA32.Float", map.GetPixelFloat(0, 0), Grayscale);
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("RGBA32_Color")]
    public void TestColor()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            assertColorEquals("RGBA32.Color", map.GetPixelColor(0, 0), new Color(Rf, Gf, Bf, Af));
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("RGBA32_Color32")]
    public void TestColor32()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            assertColor32Equals(
                "RGBA32.Color32",
                map.GetPixelColor32(0, 0),
                new Color32(R, G, B, A)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("RGBA32_HeightAlpha")]
    public void TestHeightAlpha()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            assertHeightAlphaEquals(
                "RGBA32.HA",
                map.GetPixelHeightAlpha(0, 0),
                new HeightAlpha(Rf, Af)
            );
        }
        finally
        {
            data.Dispose();
        }
    }
}
