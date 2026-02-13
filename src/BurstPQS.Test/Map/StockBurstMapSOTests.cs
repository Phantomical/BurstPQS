using System;
using BurstPQS.Map;
using KSP.Testing;
using UnityEngine;

namespace BurstPQS.Test.Map;

/// <summary>
/// Tests <see cref="StockBurstMapSO"/> against stock <see cref="MapSO"/> to verify
/// that the Burst-compatible wrapper produces identical results for all pixel lookup methods.
/// </summary>
public class StockBurstMapSOTests : BurstPQSTestBase
{
    const int W = 8;
    const int H = 8;

    void TestDepth(int bpp, string depthName)
    {
        var data = MakeGradientData(W, H, bpp);
        var stockMap = CreateMapSO(data, W, H, bpp);
        var burstMap = new StockBurstMapSO(stockMap);

        try
        {
            // Test GetPixelFloat(int,int) for all pixels
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float stockVal = stockMap.GetPixelFloat(x, y);
                    float burstVal = burstMap.GetPixelFloat(x, y);
                    assertFloatEquals($"{depthName}.GetPixelFloat({x},{y})", burstVal, stockVal);
                }
            }

            // Test GetPixelColor(int,int) for representative pixels
            int[] testX = { 0, 1, W / 2, W - 1 };
            int[] testY = { 0, 1, H / 2, H - 1 };
            foreach (int y in testY)
            {
                foreach (int x in testX)
                {
                    Color stockColor = stockMap.GetPixelColor(x, y);
                    Color burstColor = burstMap.GetPixelColor(x, y);
                    assertColorEquals(
                        $"{depthName}.GetPixelColor({x},{y})",
                        burstColor,
                        stockColor
                    );
                }
            }

            // Test GetPixelHeightAlpha(int,int) for representative pixels
            foreach (int y in testY)
            {
                foreach (int x in testX)
                {
                    var stockHA = stockMap.GetPixelHeightAlpha(x, y);
                    var burstHA = burstMap.GetPixelHeightAlpha(x, y);
                    assertHeightAlphaEquals(
                        $"{depthName}.GetPixelHeightAlpha({x},{y})",
                        burstHA,
                        stockHA
                    );
                }
            }

            // Note: Stock GetPixelColor32 for bpp==2 has a known bug (uses uninitialized field).
            // Only test Color32 for bpp != 2.
            if (bpp != 2)
            {
                foreach (int y in testY)
                {
                    foreach (int x in testX)
                    {
                        Color32 stockC32 = stockMap.GetPixelColor32(x, y);
                        Color32 burstC32 = burstMap.GetPixelColor32(x, y);
                        assertColor32Equals(
                            $"{depthName}.GetPixelColor32({x},{y})",
                            burstC32,
                            stockC32
                        );
                    }
                }
            }
        }
        finally
        {
            burstMap.Dispose();
            UnityEngine.Object.Destroy(stockMap);
        }
    }

    [TestInfo("StockBurstMapSO_Greyscale")]
    public void TestGreyscale()
    {
        TestDepth(1, "Greyscale");
    }

    [TestInfo("StockBurstMapSO_HeightAlpha")]
    public void TestHeightAlpha()
    {
        TestDepth(2, "HeightAlpha");
    }

    [TestInfo("StockBurstMapSO_RGB")]
    public void TestRGB()
    {
        TestDepth(3, "RGB");
    }

    [TestInfo("StockBurstMapSO_RGBA")]
    public void TestRGBA()
    {
        TestDepth(4, "RGBA");
    }

    [TestInfo("StockBurstMapSO_BilinearFloat")]
    public void TestBilinearFloat()
    {
        // Use bpp=1 (Greyscale) since stock has optimized path
        var data = MakeGradientData(W, H, 1);
        var stockMap = CreateMapSO(data, W, H, 1);
        var burstMap = new StockBurstMapSO(stockMap);

        try
        {
            // Test float coords in the interior (avoid edges where Kopernicus y-clamp differs)
            float[] testCoords = { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f };
            foreach (float fy in testCoords)
            {
                foreach (float fx in testCoords)
                {
                    float stockVal = stockMap.GetPixelFloat(fx, fy);
                    float burstVal = burstMap.GetPixelFloat(fx, fy);
                    assertFloatEquals($"Greyscale.BilinearFloat({fx},{fy})", burstVal, stockVal);
                }
            }
        }
        finally
        {
            burstMap.Dispose();
            UnityEngine.Object.Destroy(stockMap);
        }
    }

    [TestInfo("StockBurstMapSO_BilinearColor")]
    public void TestBilinearColor()
    {
        var data = MakeGradientData(W, H, 4);
        var stockMap = CreateMapSO(data, W, H, 4);
        var burstMap = new StockBurstMapSO(stockMap);

        try
        {
            float[] testCoords = { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f };
            foreach (float fy in testCoords)
            {
                foreach (float fx in testCoords)
                {
                    Color stockColor = stockMap.GetPixelColor(fx, fy);
                    Color burstColor = burstMap.GetPixelColor(fx, fy);
                    assertColorEquals($"RGBA.BilinearColor({fx},{fy})", burstColor, stockColor);
                }
            }
        }
        finally
        {
            burstMap.Dispose();
            UnityEngine.Object.Destroy(stockMap);
        }
    }

    [TestInfo("StockBurstMapSO_BilinearHeightAlpha")]
    public void TestBilinearHeightAlpha()
    {
        var data = MakeGradientData(W, H, 2);
        var stockMap = CreateMapSO(data, W, H, 2);
        var burstMap = new StockBurstMapSO(stockMap);

        try
        {
            float[] testCoords = { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f };
            foreach (float fy in testCoords)
            {
                foreach (float fx in testCoords)
                {
                    var stockHA = stockMap.GetPixelHeightAlpha(fx, fy);
                    var burstHA = burstMap.GetPixelHeightAlpha(fx, fy);
                    assertHeightAlphaEquals($"HeightAlpha.Bilinear({fx},{fy})", burstHA, stockHA);
                }
            }
        }
        finally
        {
            burstMap.Dispose();
            UnityEngine.Object.Destroy(stockMap);
        }
    }

    [TestInfo("StockBurstMapSO_DoubleCoords")]
    public void TestDoubleCoords()
    {
        var data = MakeGradientData(W, H, 3);
        var stockMap = CreateMapSO(data, W, H, 3);
        var burstMap = new StockBurstMapSO(stockMap);

        try
        {
            double[] testCoords = { 0.1, 0.25, 0.5, 0.75, 0.9 };
            foreach (double dy in testCoords)
            {
                foreach (double dx in testCoords)
                {
                    float stockFloat = stockMap.GetPixelFloat(dx, dy);
                    float burstFloat = burstMap.GetPixelFloat(dx, dy);
                    assertFloatEquals($"RGB.DoubleFloat({dx},{dy})", burstFloat, stockFloat);

                    Color stockColor = stockMap.GetPixelColor(dx, dy);
                    Color burstColor = burstMap.GetPixelColor(dx, dy);
                    assertColorEquals($"RGB.DoubleColor({dx},{dy})", burstColor, stockColor);
                }
            }
        }
        finally
        {
            burstMap.Dispose();
            UnityEngine.Object.Destroy(stockMap);
        }
    }

    [TestInfo("StockBurstMapSO_WidthHeight")]
    public void TestWidthHeight()
    {
        var data = MakeGradientData(16, 32, 3);
        var stockMap = CreateMapSO(data, 16, 32, 3);
        var burstMap = new StockBurstMapSO(stockMap);

        try
        {
            assertEquals("Width", burstMap.Width, 16);
            assertEquals("Height", burstMap.Height, 32);
            assertEquals("BitsPerPixel", burstMap.BitsPerPixel, 3);
            assertEquals("RowWidth", burstMap.RowWidth, 16 * 3);
        }
        finally
        {
            burstMap.Dispose();
            UnityEngine.Object.Destroy(stockMap);
        }
    }
}
