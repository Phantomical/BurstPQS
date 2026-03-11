using System;
using BurstPQS.Map;
using KSP.Testing;
using KSPTextureLoader;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Test.Map;

/// <summary>
/// Tests bilinear interpolation in <see cref="MapSODefaults"/> for correctness
/// of coordinate wrapping/clamping and interpolation weights.
/// </summary>
public class BilinearInterpolationTests : BurstPQSTestBase
{
    /// <summary>
    /// Creates a 2x2 RGBA32 TextureMapSO with 4 distinct known colors.
    /// </summary>
    static (TextureMapSO.RGBA32 map, NativeArray<byte> nativeData) Make2x2(
        Color32 c00,
        Color32 c10,
        Color32 c01,
        Color32 c11
    )
    {
        // RGBA32 layout: row-major, bottom-to-top (y=0 is bottom row)
        var data = new byte[2 * 2 * 4];
        void Write(int x, int y, Color32 c)
        {
            int i = (y * 2 + x) * 4;
            data[i] = c.r;
            data[i + 1] = c.g;
            data[i + 2] = c.b;
            data[i + 3] = c.a;
        }
        Write(0, 0, c00);
        Write(1, 0, c10);
        Write(0, 1, c01);
        Write(1, 1, c11);

        var nativeData = new NativeArray<byte>(data, Allocator.Persistent);
        return (
            new TextureMapSO.RGBA32(new CPUTexture2D.RGBA32(nativeData, 2, 2, 1)),
            nativeData
        );
    }

    [TestInfo("Bilinear_XWrapping")]
    public void TestXWrapping()
    {
        // X coordinate should wrap: x=1.0 should be equivalent to x=0.0
        var (map, nativeData) = Make2x2(
            new Color32(255, 0, 0, 255), // (0,0) red
            new Color32(0, 255, 0, 255), // (1,0) green
            new Color32(0, 0, 255, 255), // (0,1) blue
            new Color32(255, 255, 0, 255) // (1,1) yellow
        );

        try
        {
            // x=0.0 and x=1.0 should give the same result
            Color at0 = map.GetPixelColor(0.0f, 0.5f);
            Color at1 = map.GetPixelColor(1.0f, 0.5f);
            assertColorEquals("XWrap: x=0 vs x=1", at0, at1, 0.01f);

            // x=0.25 should be midpoint between left and right columns on a 2x2 texture
            // (centerX = 0.25 * 2 = 0.5, midpoint between pixel 0 and pixel 1)
            Color mid = map.GetPixelColor(0.25f, 0.0f);
            // At y=0, this interpolates between (0,0)=red and (1,0)=green at x midpoint
            // which should be roughly (0.5, 0.5, 0, 1) - yellow/brown
            assertFloatEquals("XMid.r", mid.r, 0.5f, 0.05f);
            assertFloatEquals("XMid.g", mid.g, 0.5f, 0.05f);
        }
        finally
        {
            nativeData.Dispose();
        }
    }

    [TestInfo("Bilinear_YClamping")]
    public void TestYClamping()
    {
        // Y coordinate should clamp (Kopernicus behavior): y<0 clamps to 0, y>1 clamps to 1
        var (map, nativeData) = Make2x2(
            new Color32(100, 0, 0, 255), // (0,0)
            new Color32(100, 0, 0, 255), // (1,0)
            new Color32(0, 200, 0, 255), // (0,1)
            new Color32(0, 200, 0, 255) // (1,1)
        );

        try
        {
            // y=-0.5 should clamp to y=0
            Color atNeg = map.GetPixelColor(0.0f, -0.5f);
            Color at0 = map.GetPixelColor(0.0f, 0.0f);
            assertColorEquals("YClamp: y=-0.5 vs y=0", atNeg, at0, 0.01f);

            // y=1.5 should clamp to y=1
            Color atOver = map.GetPixelColor(0.0f, 1.5f);
            Color at1 = map.GetPixelColor(0.0f, 1.0f);
            assertColorEquals("YClamp: y=1.5 vs y=1", atOver, at1, 0.01f);
        }
        finally
        {
            nativeData.Dispose();
        }
    }

    [TestInfo("Bilinear_Interpolation")]
    public void TestInterpolation()
    {
        // Test that bilinear interpolation produces correct midpoints
        var (map, nativeData) = Make2x2(
            new Color32(0, 0, 0, 255), // (0,0) black
            new Color32(255, 0, 0, 255), // (1,0) red
            new Color32(0, 255, 0, 255), // (0,1) green
            new Color32(255, 255, 0, 255) // (1,1) yellow
        );

        try
        {
            // Midpoint (0.25, 0.25) should be average of all 4 corners on a 2x2 texture
            // (centerX = 0.25 * 2 = 0.5, centerY = 0.25 * 2 = 0.5)
            Color center = map.GetPixelColor(0.25f, 0.25f);
            Color expected = new Color(0.5f, 0.5f, 0f, 1f);
            assertColorEquals("Center", center, expected, 0.05f);
        }
        finally
        {
            nativeData.Dispose();
        }
    }

    [TestInfo("Bilinear_FloatVsDouble")]
    public void TestFloatVsDouble()
    {
        // Float and double coords should produce the same results
        var (map, nativeData) = Make2x2(
            new Color32(50, 100, 150, 200),
            new Color32(200, 150, 100, 50),
            new Color32(100, 200, 50, 150),
            new Color32(150, 50, 200, 100)
        );

        try
        {
            float[] coords = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
            foreach (float fy in coords)
            {
                foreach (float fx in coords)
                {
                    float fResult = map.GetPixelFloat(fx, fy);
                    float dResult = map.GetPixelFloat((double)fx, (double)fy);
                    assertFloatEquals($"FloatVsDouble({fx},{fy})", fResult, dResult, 0.001f);

                    Color fColor = map.GetPixelColor(fx, fy);
                    Color dColor = map.GetPixelColor((double)fx, (double)fy);
                    assertColorEquals($"FloatVsDoubleColor({fx},{fy})", fColor, dColor, 0.001f);
                }
            }
        }
        finally
        {
            nativeData.Dispose();
        }
    }

    [TestInfo("Bilinear_GetPixelHeightAlpha")]
    public void TestBilinearHeightAlpha()
    {
        var (map, nativeData) = Make2x2(
            new Color32(50, 0, 0, 200),
            new Color32(100, 0, 0, 150),
            new Color32(150, 0, 0, 100),
            new Color32(200, 0, 0, 50)
        );

        try
        {
            // Midpoint (0.25, 0.25) should interpolate height and alpha independently
            // on a 2x2 texture (centerX = 0.25*2 = 0.5, centerY = 0.25*2 = 0.5)
            HeightAlpha center = map.GetPixelHeightAlpha(0.25f, 0.25f);
            // height = R channel = avg of 50,100,150,200 / 255 ~ 125/255 ~ 0.49
            // alpha = A channel = avg of 200,150,100,50 / 255 ~ 125/255 ~ 0.49
            assertFloatEquals("CenterHA.height", center.height, 125f / 255f, 0.05f);
            assertFloatEquals("CenterHA.alpha", center.alpha, 125f / 255f, 0.05f);
        }
        finally
        {
            nativeData.Dispose();
        }
    }
}
