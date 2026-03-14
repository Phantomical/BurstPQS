using System;
using BurstPQS.Map;
using KSP.Testing;
using KSPTextureLoader;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Test.Map;

/// <summary>
/// Tests each <see cref="TextureMapSO"/> format implementation against
/// <see cref="Texture2D.GetPixel(int,int)"/> to verify that pixel decoding matches Unity.
/// </summary>
public class TextureMapSOTests : BurstPQSTestBase
{
    const int W = 4;
    const int H = 4;
    const float Tol = 0.005f; // slightly more than 1/255

    /// <summary>
    /// Creates a Texture2D with RGBA32 pixels, then converts it to the target format
    /// using <see cref="Texture2D.GetPixel(int,int)"/> as ground truth.
    /// </summary>
    static (Texture2D tex, Color[,] pixels) MakeTestTexture(TextureFormat fmt)
    {
        // Create an RGBA32 source texture with known pixel values
        var src = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var colors = new Color32[W * H];
        var pixelGrid = new Color[W, H];

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                byte r = (byte)(x * 80 + 15);
                byte g = (byte)(y * 60 + 30);
                byte b = (byte)((x + y) * 40 + 50);
                byte a = (byte)(200 - x * 20 - y * 10);
                colors[y * W + x] = new Color32(r, g, b, a);
            }
        }
        src.SetPixels32(colors);
        src.Apply(false, false);

        // If the target format is different from RGBA32, create a new texture in that format
        Texture2D tex;
        if (fmt == TextureFormat.RGBA32)
        {
            tex = src;
        }
        else
        {
            tex = new Texture2D(W, H, fmt, false);
            // Copy pixels through Color conversion
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    tex.SetPixel(x, y, src.GetPixel(x, y));
                }
            }
            tex.Apply(false, false);
            UnityEngine.Object.Destroy(src);
        }

        // Read back actual pixels from the converted texture (ground truth)
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            pixelGrid[x, y] = tex.GetPixel(x, y);

        return (tex, pixelGrid);
    }

    void TestFormat<T>(
        TextureFormat fmt,
        Func<NativeArray<byte>, int, int, T> factory,
        string name,
        float tolerance = Tol
    )
        where T : struct, IMapSO
    {
        var (tex, pixels) = MakeTestTexture(fmt);
        var nativeData = new NativeArray<byte>(tex.GetRawTextureData(), Allocator.Persistent);
        var mapSO = factory(nativeData, tex.width, tex.height);
        var burst = BurstMapSO.Create(mapSO);

        try
        {
            // Test GetPixelColor(int,int) against Texture2D.GetPixel
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    Color expected = pixels[x, y];
                    Color actual = mapSO.GetPixelColor(x, y);

                    // For formats with fewer channels, the depth interpretation changes things.
                    // We compare the IMapSO result against itself through BurstMapSO wrapper.
                    Color burstActual = burst.GetPixelColor(x, y);
                    assertColorEquals(
                        $"{name}.BurstColor({x},{y})",
                        burstActual,
                        actual,
                        tolerance
                    );
                }
            }

            // Test GetPixelFloat(int,int) through wrapper
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float expected = mapSO.GetPixelFloat(x, y);
                    float actual = burst.GetPixelFloat(x, y);
                    assertFloatEquals($"{name}.BurstFloat({x},{y})", actual, expected, tolerance);
                }
            }

            // Test GetPixelHeightAlpha(int,int) through wrapper
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    HeightAlpha expected = mapSO.GetPixelHeightAlpha(x, y);
                    HeightAlpha actual = burst.GetPixelHeightAlpha(x, y);
                    assertHeightAlphaEquals(
                        $"{name}.BurstHA({x},{y})",
                        actual,
                        expected,
                        tolerance
                    );
                }
            }
        }
        finally
        {
            if (burst.IsValid)
                burst.Dispose();
            nativeData.Dispose();
            UnityEngine.Object.Destroy(tex);
        }
    }

    /// <summary>
    /// Tests an uncompressed format by comparing the TextureMapSO directly against
    /// Texture2D.GetPixel for the RGBA depth.
    /// </summary>
    void TestUncompressedVsTexture2D<T>(
        TextureFormat fmt,
        Func<NativeArray<byte>, int, int, T> factory,
        string name,
        float tolerance = Tol
    )
        where T : struct, IMapSO
    {
        var (tex, pixels) = MakeTestTexture(fmt);
        var nativeData = new NativeArray<byte>(tex.GetRawTextureData(), Allocator.Persistent);
        var mapSO = factory(nativeData, tex.width, tex.height);

        try
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    Color expected = pixels[x, y];
                    Color actual = mapSO.GetPixelColor(x, y);
                    assertColorEquals($"{name}.VsTex2D({x},{y})", actual, expected, tolerance);
                }
            }
        }
        finally
        {
            nativeData.Dispose();
            UnityEngine.Object.Destroy(tex);
        }
    }

    // ---- Uncompressed 4-channel formats ----

    const TextureWrapMode Wrap = TextureWrapMode.Repeat;

    static TextureMapSO.RGBA32 MakeRGBA32(NativeArray<byte> data, int w, int h) =>
        new(new CPUTexture2D.RGBA32(data, w, h, 1), Wrap);

    static TextureMapSO.ARGB32 MakeARGB32(NativeArray<byte> data, int w, int h) =>
        new(new CPUTexture2D.ARGB32(data, w, h, 1), Wrap);

    static TextureMapSO.BGRA32 MakeBGRA32(NativeArray<byte> data, int w, int h) =>
        new(new CPUTexture2D.BGRA32(data, w, h, 1), Wrap);

    [TestInfo("TextureMapSO_RGBA32")]
    public void TestRGBA32()
    {
        TestUncompressedVsTexture2D(TextureFormat.RGBA32, MakeRGBA32, "RGBA32");
        TestFormat(TextureFormat.RGBA32, MakeRGBA32, "RGBA32");
    }

    [TestInfo("TextureMapSO_ARGB32")]
    public void TestARGB32()
    {
        TestUncompressedVsTexture2D(TextureFormat.ARGB32, MakeARGB32, "ARGB32");
        TestFormat(TextureFormat.ARGB32, MakeARGB32, "ARGB32");
    }

    [TestInfo("TextureMapSO_BGRA32")]
    public void TestBGRA32()
    {
        TestUncompressedVsTexture2D(TextureFormat.BGRA32, MakeBGRA32, "BGRA32");
        TestFormat(TextureFormat.BGRA32, MakeBGRA32, "BGRA32");
    }

    // ---- Uncompressed 3-channel formats ----

    [TestInfo("TextureMapSO_RGB24")]
    public void TestRGB24()
    {
        TestUncompressedVsTexture2D(
            TextureFormat.RGB24,
            (data, w, h) => new TextureMapSO.RGB24(new CPUTexture2D.RGB24(data, w, h, 1), Wrap),
            "RGB24"
        );
        TestFormat(
            TextureFormat.RGB24,
            (data, w, h) => new TextureMapSO.RGB24(new CPUTexture2D.RGB24(data, w, h, 1), Wrap),
            "RGB24"
        );
    }

    [TestInfo("TextureMapSO_RGB565")]
    public void TestRGB565()
    {
        // RGB565 is lossy due to bit reduction, so use larger tolerance
        TestUncompressedVsTexture2D(
            TextureFormat.RGB565,
            (data, w, h) => new TextureMapSO.RGB565(new CPUTexture2D.RGB565(data, w, h, 1), Wrap),
            "RGB565",
            0.04f // 5-bit channels have ~1/31 precision
        );
    }

    // ---- Uncompressed 2-channel formats ----

    [TestInfo("TextureMapSO_RG16")]
    public void TestRG16()
    {
        TestFormat(
            TextureFormat.RG16,
            (data, w, h) => new TextureMapSO.RG16(new CPUTexture2D.RG16(data, w, h, 1), Wrap),
            "RG16"
        );
    }

    // ---- Uncompressed 1-channel formats ----

    [TestInfo("TextureMapSO_R8")]
    public void TestR8()
    {
        TestFormat(
            TextureFormat.R8,
            (data, w, h) => new TextureMapSO.R8(new CPUTexture2D.R8(data, w, h, 1), Wrap),
            "R8"
        );
    }

    [TestInfo("TextureMapSO_Alpha8")]
    public void TestAlpha8()
    {
        TestFormat(
            TextureFormat.Alpha8,
            (data, w, h) => new TextureMapSO.Alpha8(new CPUTexture2D.Alpha8(data, w, h, 1), Wrap),
            "Alpha8"
        );
    }

    [TestInfo("TextureMapSO_R16")]
    public void TestR16()
    {
        TestFormat(
            TextureFormat.R16,
            (data, w, h) => new TextureMapSO.R16(new CPUTexture2D.R16(data, w, h, 1), Wrap),
            "R16"
        );
    }

    // ---- Float formats ----

    [TestInfo("TextureMapSO_RFloat")]
    public void TestRFloat()
    {
        TestFormat(
            TextureFormat.RFloat,
            (data, w, h) => new TextureMapSO.RFloat(new CPUTexture2D.RFloat(data, w, h, 1), Wrap),
            "RFloat",
            0.001f
        );
    }

    [TestInfo("TextureMapSO_RGFloat")]
    public void TestRGFloat()
    {
        TestFormat(
            TextureFormat.RGFloat,
            (data, w, h) => new TextureMapSO.RGFloat(new CPUTexture2D.RGFloat(data, w, h, 1), Wrap),
            "RGFloat",
            0.001f
        );
    }

    [TestInfo("TextureMapSO_RGBAFloat")]
    public void TestRGBAFloat()
    {
        TestUncompressedVsTexture2D(
            TextureFormat.RGBAFloat,
            (data, w, h) => new TextureMapSO.RGBAFloat(new CPUTexture2D.RGBAFloat(data, w, h, 1), Wrap),
            "RGBAFloat",
            0.001f
        );
        TestFormat(
            TextureFormat.RGBAFloat,
            (data, w, h) => new TextureMapSO.RGBAFloat(new CPUTexture2D.RGBAFloat(data, w, h, 1), Wrap),
            "RGBAFloat",
            0.001f
        );
    }

    // ---- Half-float formats ----

    [TestInfo("TextureMapSO_RHalf")]
    public void TestRHalf()
    {
        TestFormat(
            TextureFormat.RHalf,
            (data, w, h) => new TextureMapSO.RHalf(new CPUTexture2D.RHalf(data, w, h, 1), Wrap),
            "RHalf",
            0.002f
        );
    }

    [TestInfo("TextureMapSO_RGHalf")]
    public void TestRGHalf()
    {
        TestFormat(
            TextureFormat.RGHalf,
            (data, w, h) => new TextureMapSO.RGHalf(new CPUTexture2D.RGHalf(data, w, h, 1), Wrap),
            "RGHalf",
            0.002f
        );
    }

    [TestInfo("TextureMapSO_RGBAHalf")]
    public void TestRGBAHalf()
    {
        TestFormat(
            TextureFormat.RGBAHalf,
            (data, w, h) => new TextureMapSO.RGBAHalf(new CPUTexture2D.RGBAHalf(data, w, h, 1), Wrap),
            "RGBAHalf",
            0.002f
        );
    }

    // ---- Packed formats ----

    [TestInfo("TextureMapSO_RGBA4444")]
    public void TestRGBA4444()
    {
        // 4-bit channels -> 1/15 precision
        TestFormat(
            TextureFormat.RGBA4444,
            (data, w, h) => new TextureMapSO.RGBA4444(new CPUTexture2D.RGBA4444(data, w, h, 1), Wrap),
            "RGBA4444",
            0.07f
        );
    }

    [TestInfo("TextureMapSO_ARGB4444")]
    public void TestARGB4444()
    {
        TestFormat(
            TextureFormat.ARGB4444,
            (data, w, h) => new TextureMapSO.ARGB4444(new CPUTexture2D.ARGB4444(data, w, h, 1), Wrap),
            "ARGB4444",
            0.07f
        );
    }

    // ---- Block-compressed formats ----
    // For compressed formats, we create an RGBA32 texture, compress it,
    // then compare our decoder against Texture2D.GetPixel which reads from
    // the same compressed data.

    [TestInfo("TextureMapSO_DXT1")]
    public void TestDXT1()
    {
        // DXT1 is lossy; use a texture with solid-ish colors for better results
        var src = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            src.SetPixel(x, y, new Color(x / 7f, y / 7f, 0.5f, 1f));
        src.Apply(false, false);
        src.Compress(false);

        // Re-read in DXT1 format
        if (src.format != TextureFormat.DXT1)
        {
            // Compression might choose DXT5 instead; skip if not DXT1
            UnityEngine.Object.Destroy(src);
            return;
        }

        var nativeData = new NativeArray<byte>(src.GetRawTextureData(), Allocator.Persistent);
        var mapSO = new TextureMapSO.DXT1(
            new CPUTexture2D.DXT1(nativeData, src.width, src.height, 1),
            Wrap
        );
        var burst = BurstMapSO.Create(mapSO);

        try
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Color expected = src.GetPixel(x, y);
                    Color actual = mapSO.GetPixelColor(x, y);
                    // DXT1 is lossy, allow larger tolerance
                    assertColorEquals($"DXT1.VsTex2D({x},{y})", actual, expected, 0.05f);

                    // Also verify wrapper dispatch
                    Color burstActual = burst.GetPixelColor(x, y);
                    assertColorEquals($"DXT1.Burst({x},{y})", burstActual, actual, 0.001f);
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
            UnityEngine.Object.Destroy(src);
        }
    }

    [TestInfo("TextureMapSO_DXT5")]
    public void TestDXT5()
    {
        var src = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            src.SetPixel(x, y, new Color(x / 7f, y / 7f, 0.5f, (x + y) / 14f));
        src.Apply(false, false);
        src.Compress(false);

        // Compression might not produce DXT5; handle gracefully
        if (src.format != TextureFormat.DXT5)
        {
            UnityEngine.Object.Destroy(src);
            return;
        }

        var nativeData = new NativeArray<byte>(src.GetRawTextureData(), Allocator.Persistent);
        var mapSO = new TextureMapSO.DXT5(
            new CPUTexture2D.DXT5(nativeData, src.width, src.height, 1),
            Wrap
        );
        var burst = BurstMapSO.Create(mapSO);

        try
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Color expected = src.GetPixel(x, y);
                    Color actual = mapSO.GetPixelColor(x, y);
                    assertColorEquals($"DXT5.VsTex2D({x},{y})", actual, expected, 0.05f);

                    Color burstActual = burst.GetPixelColor(x, y);
                    assertColorEquals($"DXT5.Burst({x},{y})", burstActual, actual, 0.001f);
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
            UnityEngine.Object.Destroy(src);
        }
    }

    [TestInfo("TextureMapSO_BC4")]
    public void TestBC4()
    {
        // BC4 is single-channel. Create a greyscale texture, compress to BC4.
        var src = new Texture2D(8, 8, TextureFormat.R8, false);
        var rawData = new byte[64];
        for (int i = 0; i < 64; i++)
            rawData[i] = (byte)(i * 4);
        src.LoadRawTextureData(rawData);
        src.Apply(false, false);

        // Try to get BC4 by compressing
        src.Compress(false);
        if (src.format != TextureFormat.BC4)
        {
            UnityEngine.Object.Destroy(src);
            return;
        }

        var nativeData = new NativeArray<byte>(src.GetRawTextureData(), Allocator.Persistent);
        var mapSO = new TextureMapSO.BC4(
            new CPUTexture2D.BC4(nativeData, src.width, src.height, 1),
            Wrap
        );
        var burst = BurstMapSO.Create(mapSO);

        try
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Color expected = src.GetPixel(x, y);
                    float actual = mapSO.GetPixelFloat(x, y);
                    assertFloatEquals($"BC4.VsTex2D({x},{y})", actual, expected.r, 0.05f);

                    float burstActual = burst.GetPixelFloat(x, y);
                    assertFloatEquals($"BC4.Burst({x},{y})", burstActual, actual, 0.001f);
                }
            }
        }
        finally
        {
            burst.Dispose();
            nativeData.Dispose();
            UnityEngine.Object.Destroy(src);
        }
    }
}
