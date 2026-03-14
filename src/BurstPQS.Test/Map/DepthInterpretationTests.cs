using System;
using BurstPQS.Map;
using KSP.Testing;
using KSPTextureLoader;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Test.Map;

/// <summary>
/// Tests that depth interpretation (Greyscale, HeightAlpha, RGB, RGBA) produces
/// correct results for GetPixelFloat, GetPixelColor, GetPixelColor32, and GetPixelHeightAlpha.
/// Uses a known RGBA32 pixel to verify each interpretation.
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

    static (TextureMapSO.RGBA32 map, NativeArray<byte> data) MakeSinglePixelRGBA32()
    {
        var bytes = new byte[] { R, G, B, A };
        var data = new NativeArray<byte>(bytes, Allocator.Persistent);
        return (new TextureMapSO.RGBA32(new CPUTexture2D.RGBA32(data, 1, 1, 1)), data);
    }

    // ---- Greyscale depth ----

    [TestInfo("Depth_Greyscale_Float")]
    public void TestGreyscaleFloat()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // Greyscale: float = R
            assertFloatEquals("Greyscale.Float", map.GetPixelFloat(0, 0), Rf);
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_Greyscale_Color")]
    public void TestGreyscaleColor()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // Greyscale: Color = (R, R, R, 1)
            assertColorEquals(
                "Greyscale.Color",
                map.GetPixelColor(0, 0),
                new Color(Rf, Rf, Rf, 1f)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_Greyscale_Color32")]
    public void TestGreyscaleColor32()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // Greyscale: Color32 = (R, R, R, 255)
            assertColor32Equals(
                "Greyscale.Color32",
                map.GetPixelColor32(0, 0),
                new Color32(R, R, R, 255)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_Greyscale_HeightAlpha")]
    public void TestGreyscaleHeightAlpha()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // Greyscale: HeightAlpha = (R, 1)
            assertHeightAlphaEquals(
                "Greyscale.HA",
                map.GetPixelHeightAlpha(0, 0),
                new HeightAlpha(Rf, 1f)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- HeightAlpha depth ----

    [TestInfo("Depth_HeightAlpha_Float")]
    public void TestHeightAlphaFloat()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // HeightAlpha: float = (R + A) * 0.5
            assertFloatEquals("HeightAlpha.Float", map.GetPixelFloat(0, 0), (Rf + Af) * 0.5f);
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_HeightAlpha_Color")]
    public void TestHeightAlphaColor()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // HeightAlpha: Color = (R, R, R, A)
            assertColorEquals(
                "HeightAlpha.Color",
                map.GetPixelColor(0, 0),
                new Color(Rf, Rf, Rf, Af)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_HeightAlpha_Color32")]
    public void TestHeightAlphaColor32()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // HeightAlpha: Color32 = (R, R, R, A)
            assertColor32Equals(
                "HeightAlpha.Color32",
                map.GetPixelColor32(0, 0),
                new Color32(R, R, R, A)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_HeightAlpha_HA")]
    public void TestHeightAlphaHA()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // HeightAlpha: HeightAlpha = (R, A)
            assertHeightAlphaEquals(
                "HeightAlpha.HA",
                map.GetPixelHeightAlpha(0, 0),
                new HeightAlpha(Rf, Af)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- RGB depth ----

    [TestInfo("Depth_RGB_Float")]
    public void TestRGBFloat()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGB: float = (R + G + B) / 3
            assertFloatEquals("RGB.Float", map.GetPixelFloat(0, 0), (Rf + Gf + Bf) * (1f / 3f));
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_RGB_Color")]
    public void TestRGBColor()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGB: Color = (R, G, B, 1)
            assertColorEquals("RGB.Color", map.GetPixelColor(0, 0), new Color(Rf, Gf, Bf, 1f));
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_RGB_Color32")]
    public void TestRGBColor32()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGB: Color32 = (R, G, B, 255)
            assertColor32Equals(
                "RGB.Color32",
                map.GetPixelColor32(0, 0),
                new Color32(R, G, B, 255)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_RGB_HeightAlpha")]
    public void TestRGBHeightAlpha()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGB: HeightAlpha = (R, 1)
            assertHeightAlphaEquals(
                "RGB.HA",
                map.GetPixelHeightAlpha(0, 0),
                new HeightAlpha(Rf, 1f)
            );
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- RGBA depth ----

    [TestInfo("Depth_RGBA_Float")]
    public void TestRGBAFloat()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGBA: float = (R + G + B + A) / 4
            assertFloatEquals("RGBA.Float", map.GetPixelFloat(0, 0), (Rf + Gf + Bf + Af) * 0.25f);
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_RGBA_Color")]
    public void TestRGBAColor()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGBA: Color = (R, G, B, A)
            assertColorEquals("RGBA.Color", map.GetPixelColor(0, 0), new Color(Rf, Gf, Bf, Af));
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_RGBA_Color32")]
    public void TestRGBAColor32()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGBA: Color32 = (R, G, B, A)
            assertColor32Equals("RGBA.Color32", map.GetPixelColor32(0, 0), new Color32(R, G, B, A));
        }
        finally
        {
            data.Dispose();
        }
    }

    [TestInfo("Depth_RGBA_HeightAlpha")]
    public void TestRGBAHeightAlpha()
    {
        var (map, data) = MakeSinglePixelRGBA32();
        try
        {
            // RGBA: HeightAlpha = (R, A)
            assertHeightAlphaEquals(
                "RGBA.HA",
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
