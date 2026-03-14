using System;
using BurstPQS.Map;
using KSP.Testing;
using KSPTextureLoader;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Test.Map;

/// <summary>
/// Tests <see cref="BurstMapSO"/> wrapper dispatch to ensure vtable/managed paths
/// work correctly, and tests lifecycle (Create, IsValid, Dispose).
/// </summary>
public class BurstMapSOWrapperTests : BurstPQSTestBase
{
    const int W = 4;
    const int H = 4;

    static byte[] MakeRGBA32Data()
    {
        var data = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                data[i] = (byte)(x * 64); // R
                data[i + 1] = (byte)(y * 64); // G
                data[i + 2] = 128; // B
                data[i + 3] = 255; // A
            }
        }
        return data;
    }

    static (TextureMapSO.RGBA32 inner, NativeArray<byte> nativeData) MakeMapSO()
    {
        var rawData = MakeRGBA32Data();
        var nativeData = new NativeArray<byte>(rawData, Allocator.Persistent);
        var inner = new TextureMapSO.RGBA32(new CPUTexture2D.RGBA32(nativeData, W, H, 1), TextureWrapMode.Repeat);
        return (inner, nativeData);
    }

    [TestInfo("BurstMapSO_CreateIsValid")]
    public void TestCreateIsValid()
    {
        var (inner, nativeData) = MakeMapSO();
        var burst = BurstMapSO.Create(inner);

        try
        {
            assertEquals("IsValid", burst.IsValid, true);
            assertEquals("Width", burst.Width, W);
            assertEquals("Height", burst.Height, H);
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
        }
    }

    [TestInfo("BurstMapSO_InvalidDefault")]
    public void TestInvalidDefault()
    {
        var burst = new BurstMapSO();
        assertEquals("IsValid", burst.IsValid, false);
        assertFloatEquals("GetPixelFloat(0,0)", burst.GetPixelFloat(0, 0), 0f);
        assertColorEquals("GetPixelColor(0,0)", burst.GetPixelColor(0, 0), Color.black);
    }

    [TestInfo("BurstMapSO_DisposeInvalidates")]
    public void TestDisposeInvalidates()
    {
        var (inner, nativeData) = MakeMapSO();
        var burst = BurstMapSO.Create(inner);

        assertEquals("IsValid before dispose", burst.IsValid, true);
        burst.Dispose();
        assertEquals("IsValid after dispose", burst.IsValid, false);
        nativeData.Dispose();
    }

    [TestInfo("BurstMapSO_GetPixelFloat_Int")]
    public void TestGetPixelFloat_Int()
    {
        var (inner, nativeData) = MakeMapSO();
        var burst = BurstMapSO.Create(inner);

        try
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float expected = inner.GetPixelFloat(x, y);
                    float actual = burst.GetPixelFloat(x, y);
                    assertFloatEquals($"GetPixelFloat({x},{y})", actual, expected, 0.001f);
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
        }
    }

    [TestInfo("BurstMapSO_GetPixelColor_Int")]
    public void TestGetPixelColor_Int()
    {
        var (inner, nativeData) = MakeMapSO();
        var burst = BurstMapSO.Create(inner);

        try
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    Color expected = inner.GetPixelColor(x, y);
                    Color actual = burst.GetPixelColor(x, y);
                    assertColorEquals($"GetPixelColor({x},{y})", actual, expected);
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
        }
    }

    [TestInfo("BurstMapSO_GetPixelColor32_Int")]
    public void TestGetPixelColor32_Int()
    {
        var (inner, nativeData) = MakeMapSO();
        var burst = BurstMapSO.Create(inner);

        try
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    Color32 expected = inner.GetPixelColor32(x, y);
                    Color32 actual = burst.GetPixelColor32(x, y);
                    assertColor32Equals($"GetPixelColor32({x},{y})", actual, expected);
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
        }
    }

    [TestInfo("BurstMapSO_GetPixelHeightAlpha_Int")]
    public void TestGetPixelHeightAlpha_Int()
    {
        var (inner, nativeData) = MakeMapSO();
        var burst = BurstMapSO.Create(inner);

        try
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    HeightAlpha expected = inner.GetPixelHeightAlpha(x, y);
                    HeightAlpha actual = burst.GetPixelHeightAlpha(x, y);
                    assertHeightAlphaEquals($"GetPixelHeightAlpha({x},{y})", actual, expected);
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
        }
    }

    [TestInfo("BurstMapSO_BilinearDispatch")]
    public void TestBilinearDispatch()
    {
        var (inner, nativeData) = MakeMapSO();
        var burst = BurstMapSO.Create(inner);

        try
        {
            float[] coords = { 0.25f, 0.5f, 0.75f };
            foreach (float fy in coords)
            {
                foreach (float fx in coords)
                {
                    float expectedF = inner.GetPixelFloat(fx, fy);
                    float actualF = burst.GetPixelFloat(fx, fy);
                    assertFloatEquals($"BilinearFloat({fx},{fy})", actualF, expectedF, 0.001f);

                    Color expectedC = inner.GetPixelColor(fx, fy);
                    Color actualC = burst.GetPixelColor(fx, fy);
                    assertColorEquals($"BilinearColor({fx},{fy})", actualC, expectedC);

                    HeightAlpha expectedHA = inner.GetPixelHeightAlpha(fx, fy);
                    HeightAlpha actualHA = burst.GetPixelHeightAlpha(fx, fy);
                    assertHeightAlphaEquals(
                        $"BilinearHeightAlpha({fx},{fy})",
                        actualHA,
                        expectedHA
                    );
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
        }
    }

    [TestInfo("BurstMapSO_CreateFromStockMapSO")]
    public void TestCreateFromStockMapSO()
    {
        var data = MakeGradientData(W, H, 3);
        var stockMap = CreateMapSO(data, W, H, 3);

        // Register factory if not already registered (may fail if already registered)
        try
        {
            BurstMapSO.RegisterMapSOFactoryFunc<MapSO>(m =>
                BurstMapSO.Create(new StockBurstMapSO(m))
            );
        }
        catch
        {
            // Already registered by BurstPQS loader
        }

        var burst = BurstMapSO.Create(stockMap);

        try
        {
            assertEquals("IsValid", burst.IsValid, true);
            assertEquals("Width", burst.Width, W);
            assertEquals("Height", burst.Height, H);

            float stockVal = stockMap.GetPixelFloat(2, 2);
            float burstVal = burst.GetPixelFloat(2, 2);
            assertFloatEquals("StockMapSO dispatch GetPixelFloat(2,2)", burstVal, stockVal);
        }
        finally
        {
            burst.Dispose();
            UnityEngine.Object.Destroy(stockMap);
        }
    }
}
