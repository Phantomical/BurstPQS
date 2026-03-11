using System;
using BurstPQS.Map;
using KSP.Testing;
using KSPTextureLoader;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Test.Map;

/// <summary>
/// Tests the BC7 block decoder against hand-crafted blocks with analytically-computed
/// expected outputs AND against Unity's Texture2D.GetPixel as ground truth.
/// Covers all 8 modes plus partition tables, p-bits, channel rotation,
/// index mode selection, anchor index bit reduction, and interpolation weights.
/// </summary>
public class BC7DecoderTests : BurstPQSTestBase
{
    // ---- Bit writer for constructing BC7 blocks ----

    struct BitWriter(byte[] data)
    {
        readonly byte[] data = data;
        int pos = 0;

        /// <summary>Write <paramref name="count"/> bits of <paramref name="value"/>, LSB first.</summary>
        public void Write(int value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int byteIdx = pos >> 3;
                int bitIdx = pos & 7;
                data[byteIdx] |= (byte)(((value >> i) & 1) << bitIdx);
                pos++;
            }
        }

        /// <summary>Write the same value N times, each <paramref name="bits"/> wide.</summary>
        public void WriteN(int value, int bits, int n)
        {
            for (int i = 0; i < n; i++)
                Write(value, bits);
        }
    }

    // ---- Helpers ----

    /// <summary>
    /// BC7Unquantize: expand a quantized value to 8-bit.
    /// Must match TextureMapSO.BC7Unquantize exactly.
    /// </summary>
    static int Unquantize(int val, int bits)
    {
        if (bits >= 8)
            return val;
        val = val << (8 - bits);
        return val | (val >> bits);
    }

    /// <summary>
    /// BC7Interpolate: weighted blend between two 8-bit endpoints.
    /// Must match TextureMapSO.BC7Interpolate exactly.
    /// </summary>
    static int Interpolate(int e0, int e1, int weight)
    {
        return (e0 * (64 - weight) + e1 * weight + 32) >> 6;
    }

    static readonly int[] Weights2 = { 0, 21, 43, 64 };
    static readonly int[] Weights3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
    static readonly int[] Weights4 =
    {
        0,
        4,
        9,
        13,
        17,
        21,
        26,
        30,
        34,
        38,
        43,
        47,
        51,
        55,
        60,
        64,
    };

    static (TextureMapSO.BC7 bc7, NativeArray<byte> nativeData) MakeBC7(byte[] block)
    {
        var native = new NativeArray<byte>(block, Allocator.Persistent);
        return (
            new TextureMapSO.BC7(new CPUTexture2D.BC7(native, 4, 4, 1)),
            native
        );
    }

    void AssertBC7Pixel(
        string label,
        TextureMapSO.BC7 bc7,
        int x,
        int y,
        int er,
        int eg,
        int eb,
        int ea
    )
    {
        Color32 c = bc7.GetPixelColor32(x, y);
        assertColor32Equals(label, c, new Color32((byte)er, (byte)eg, (byte)eb, (byte)ea), 0);
    }

    /// <summary>
    /// Compare our BC7 decoder against Unity's Texture2D.GetPixel for all 16 pixels
    /// of a 4x4 block. This validates our decoder matches the GPU/platform reference.
    /// </summary>
    void CompareAllPixelsWithUnity(string label, byte[] block)
    {
        var tex = new Texture2D(4, 4, TextureFormat.BC7, false);
        tex.LoadRawTextureData(block);
        tex.Apply(false, false);

        var native = new NativeArray<byte>(block, Allocator.Persistent);
        var bc7 = new TextureMapSO.BC7(new CPUTexture2D.BC7(native, 4, 4, 1));

        try
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Color expected = tex.GetPixel(x, y);
                    Color actual = bc7.GetPixelColor(x, y);
                    assertColorEquals($"{label}.Unity({x},{y})", actual, expected);
                }
            }
        }
        finally
        {
            native.Dispose();
            UnityEngine.Object.Destroy(tex);
        }
    }

    // ---- Mode 0: 3 subsets, 4-bit RGB + 1 unique pbit, 3-bit indices ----
    //
    // Layout: 1 mode | 4 partition | 72 endpoints (3ch x 6ep x 4bit) | 6 pbits | 45 indices
    //
    // Solid test: partition=0, all R=10, G=5, B=2, all pbits=1
    // After pbit: (10<<1)|1=21, (5<<1)|1=11, (2<<1)|1=5 (5-bit values)
    // Unquantize(21,5)=173, Unquantize(11,5)=90, Unquantize(5,5)=41
    // All indices=0, weight=0: result = endpoint values exactly
    // Expected: R=173, G=90, B=41, A=255

    [TestInfo("BC7_Mode0_Solid")]
    public void TestMode0Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00000001, 1); // mode 0
        w.Write(0, 4); // partition 0
        w.WriteN(10, 4, 6); // R: 6 endpoints all = 10
        w.WriteN(5, 4, 6); // G: 6 endpoints all = 5
        w.WriteN(2, 4, 6); // B: 6 endpoints all = 2
        w.WriteN(1, 1, 6); // 6 p-bits all = 1
        // indices all 0 (remaining bits stay 0)

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode0(0,0)", bc7, 0, 0, 173, 90, 41, 255);
            AssertBC7Pixel("Mode0(3,3)", bc7, 3, 3, 173, 90, 41, 255);
            CompareAllPixelsWithUnity("Mode0", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 1: 2 subsets, 6-bit RGB + 1 shared pbit, 3-bit indices ----
    //
    // Layout: 2 mode | 6 partition | 72 endpoints (3ch x 4ep x 6bit) | 2 shared pbits | 46 indices
    //
    // Solid test: partition=0, all R=40, G=20, B=10, both shared pbits=0
    // After pbit: (40<<1)|0=80, (20<<1)|0=40, (10<<1)|0=20 (7-bit)
    // Unquantize(80,7)=161, Unquantize(40,7)=80, Unquantize(20,7)=40
    // Expected: R=161, G=80, B=40, A=255

    [TestInfo("BC7_Mode1_Solid")]
    public void TestMode1Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00000010, 2); // mode 1
        w.Write(0, 6); // partition 0
        w.WriteN(40, 6, 4); // R
        w.WriteN(20, 6, 4); // G
        w.WriteN(10, 6, 4); // B
        w.WriteN(0, 1, 2); // shared pbits = 0
        // indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode1(0,0)", bc7, 0, 0, 161, 80, 40, 255);
            AssertBC7Pixel("Mode1(2,1)", bc7, 2, 1, 161, 80, 40, 255);
            CompareAllPixelsWithUnity("Mode1", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 2: 3 subsets, 5-bit RGB, no pbit, 2-bit indices ----
    //
    // Layout: 3 mode | 6 partition | 90 endpoints (3ch x 6ep x 5bit) | 29 indices
    //
    // Solid test: partition=0, all R=20, G=10, B=5
    // Unquantize(20,5)=165, Unquantize(10,5)=82, Unquantize(5,5)=41
    // Expected: R=165, G=82, B=41, A=255

    [TestInfo("BC7_Mode2_Solid")]
    public void TestMode2Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00000100, 3); // mode 2
        w.Write(0, 6); // partition 0
        w.WriteN(20, 5, 6); // R
        w.WriteN(10, 5, 6); // G
        w.WriteN(5, 5, 6); // B
        // no pbits, indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode2(0,0)", bc7, 0, 0, 165, 82, 41, 255);
            AssertBC7Pixel("Mode2(1,2)", bc7, 1, 2, 165, 82, 41, 255);
            CompareAllPixelsWithUnity("Mode2", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 3: 2 subsets, 7-bit RGB + 1 unique pbit, 2-bit indices ----
    //
    // Layout: 4 mode | 6 partition | 84 endpoints (3ch x 4ep x 7bit) | 4 pbits | 30 indices
    //
    // Solid test: partition=0, all R=100, G=50, B=25, all pbits=0
    // After pbit: (100<<1)|0=200, (50<<1)|0=100, (25<<1)|0=50 (8-bit)
    // Unquantize(200,8)=200, Unquantize(100,8)=100, Unquantize(50,8)=50
    // Expected: R=200, G=100, B=50, A=255

    [TestInfo("BC7_Mode3_Solid")]
    public void TestMode3Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00001000, 4); // mode 3
        w.Write(0, 6); // partition 0
        w.WriteN(100, 7, 4); // R
        w.WriteN(50, 7, 4); // G
        w.WriteN(25, 7, 4); // B
        w.WriteN(0, 1, 4); // 4 pbits = 0
        // indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode3(0,0)", bc7, 0, 0, 200, 100, 50, 255);
            AssertBC7Pixel("Mode3(3,1)", bc7, 3, 1, 200, 100, 50, 255);
            CompareAllPixelsWithUnity("Mode3", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 4: 1 subset, 5-bit RGB + 6-bit A, rotation, idxMode, 2+3 bit indices ----
    //
    // Layout: 5 mode | 2 rotation | 1 idxMode | 30 RGB ep (2x5x3) | 12 A ep (2x6) | 31 idx2 | 47 idx3
    //
    // Solid test: rotation=0, idxMode=0, R0=R1=20, G0=G1=10, B0=B1=5, A0=A1=40
    // Unquantize(20,5)=165, Unquantize(10,5)=82, Unquantize(5,5)=41, Unquantize(40,6)=162
    // Expected: R=165, G=82, B=41, A=162

    [TestInfo("BC7_Mode4_Solid")]
    public void TestMode4Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00010000, 5); // mode 4
        w.Write(0, 2); // rotation 0
        w.Write(0, 1); // idxMode 0
        w.WriteN(20, 5, 2); // R0, R1
        w.WriteN(10, 5, 2); // G0, G1
        w.WriteN(5, 5, 2); // B0, B1
        w.WriteN(40, 6, 2); // A0, A1
        // indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode4(0,0)", bc7, 0, 0, 165, 82, 41, 162);
            AssertBC7Pixel("Mode4(2,3)", bc7, 2, 3, 165, 82, 41, 162);
            CompareAllPixelsWithUnity("Mode4", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 5: 1 subset, 7-bit RGB + 8-bit A, rotation, 2+2 bit indices ----
    //
    // Layout: 6 mode | 2 rotation | 42 RGB ep (2x7x3) | 16 A ep (2x8) | 31 color idx | 31 alpha idx
    //
    // Solid test: rotation=0, R0=R1=100, G0=G1=50, B0=B1=25, A0=A1=200
    // Unquantize(100,7)=201, Unquantize(50,7)=100, Unquantize(25,7)=50, Unquantize(200,8)=200
    // Expected: R=201, G=100, B=50, A=200

    [TestInfo("BC7_Mode5_Solid")]
    public void TestMode5Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00100000, 6); // mode 5
        w.Write(0, 2); // rotation 0
        w.WriteN(100, 7, 2); // R0, R1
        w.WriteN(50, 7, 2); // G0, G1
        w.WriteN(25, 7, 2); // B0, B1
        w.WriteN(200, 8, 2); // A0, A1
        // indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode5(0,0)", bc7, 0, 0, 201, 100, 50, 200);
            AssertBC7Pixel("Mode5(1,1)", bc7, 1, 1, 201, 100, 50, 200);
            CompareAllPixelsWithUnity("Mode5", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 6: 1 subset, 7-bit RGBA + 1 unique pbit, 4-bit indices ----
    //
    // Layout: 7 mode | 56 RGBA ep (2x7x4) | 2 pbits | 63 indices
    //
    // Solid test: R0=R1=50, G0=G1=30, B0=B1=10, A0=A1=60, pbit0=pbit1=0
    // After pbit: R=100, G=60, B=20, A=120 (8-bit, no unquantize needed)
    // Expected: R=100, G=60, B=20, A=120

    [TestInfo("BC7_Mode6_Solid")]
    public void TestMode6Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b01000000, 7); // mode 6
        w.WriteN(50, 7, 2); // R0, R1
        w.WriteN(30, 7, 2); // G0, G1
        w.WriteN(10, 7, 2); // B0, B1
        w.WriteN(60, 7, 2); // A0, A1
        w.WriteN(0, 1, 2); // pbits = 0
        // indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode6(0,0)", bc7, 0, 0, 100, 60, 20, 120);
            AssertBC7Pixel("Mode6(3,3)", bc7, 3, 3, 100, 60, 20, 120);
            CompareAllPixelsWithUnity("Mode6", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 7: 2 subsets, 5-bit RGBA + 1 unique pbit, 2-bit indices ----
    //
    // Layout: 8 mode | 6 partition | 80 endpoints (4ch x 4ep x 5bit) | 4 pbits | 30 indices
    //
    // Solid test: partition=0, all R=20, G=10, B=5, A=25, all pbits=0
    // After pbit: R=40, G=20, B=10, A=50 (6-bit)
    // Unquantize(40,6)=162, Unquantize(20,6)=81, Unquantize(10,6)=40, Unquantize(50,6)=203
    // Expected: R=162, G=81, B=40, A=203

    [TestInfo("BC7_Mode7_Solid")]
    public void TestMode7Solid()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b10000000, 8); // mode 7
        w.Write(0, 6); // partition 0
        w.WriteN(20, 5, 4); // R
        w.WriteN(10, 5, 4); // G
        w.WriteN(5, 5, 4); // B
        w.WriteN(25, 5, 4); // A
        w.WriteN(0, 1, 4); // 4 pbits = 0
        // indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode7(0,0)", bc7, 0, 0, 162, 81, 40, 203);
            AssertBC7Pixel("Mode7(2,2)", bc7, 2, 2, 162, 81, 40, 203);
            CompareAllPixelsWithUnity("Mode7", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Reserved mode (all zeros) ----

    [TestInfo("BC7_ReservedMode")]
    public void TestReservedMode()
    {
        var block = new byte[16]; // all zeros -> mode >= 8
        var (bc7, data) = MakeBC7(block);
        try
        {
            // Decoder returns r=g=b=a=0 for reserved mode
            // With RGBA depth, Color32 = (0, 0, 0, 0)
            AssertBC7Pixel("Reserved(0,0)", bc7, 0, 0, 0, 0, 0, 0);
            CompareAllPixelsWithUnity("Reserved", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 6 interpolation test ----
    //
    // Different endpoints, non-zero index for pixel (0,0).
    // R0=50, R1=100, G0=0, G1=60, B0=127, B1=0, A0=30, A1=90
    // pbit0=0, pbit1=0
    // After pbit: R0=100, R1=200, G0=0, G1=120, B0=254, B1=0, A0=60, A1=180
    // All 8-bit, no unquantize needed.
    //
    // Pixel 0 (anchor): 3-bit index, use value 4
    // Weight = BC7Weights4[4] = 17
    // R = (100*47 + 200*17 + 32) >> 6 = (4700+3400+32)>>6 = 8132>>6 = 127
    // G = (0*47 + 120*17 + 32) >> 6 = (2040+32)>>6 = 2072>>6 = 32
    // B = (254*47 + 0*17 + 32) >> 6 = (11938+32)>>6 = 11970>>6 = 187 (11970/64=186.99->186)
    // A = (60*47 + 180*17 + 32) >> 6 = (2820+3060+32)>>6 = 5912>>6 = 92

    [TestInfo("BC7_Mode6_Interpolation")]
    public void TestMode6Interpolation()
    {
        // Compute expected values via reference functions
        int r0 = (50 << 1) | 0,
            r1 = (100 << 1) | 0; // 100, 200
        int g0 = (0 << 1) | 0,
            g1 = (60 << 1) | 0; // 0, 120
        int b0 = (127 << 1) | 0,
            b1 = (0 << 1) | 0; // 254, 0
        int a0 = (30 << 1) | 0,
            a1 = (90 << 1) | 0; // 60, 180

        int w4 = Weights4[4]; // 17
        int expR = Interpolate(r0, r1, w4);
        int expG = Interpolate(g0, g1, w4);
        int expB = Interpolate(b0, b1, w4);
        int expA = Interpolate(a0, a1, w4);

        var block = new byte[16];
        var bw = new BitWriter(block);
        bw.Write(0b01000000, 7); // mode 6
        bw.Write(50, 7); // R0
        bw.Write(100, 7); // R1
        bw.Write(0, 7); // G0
        bw.Write(60, 7); // G1
        bw.Write(127, 7); // B0
        bw.Write(0, 7); // B1
        bw.Write(30, 7); // A0
        bw.Write(90, 7); // A1
        bw.Write(0, 1); // pbit0
        bw.Write(0, 1); // pbit1
        // Pixel 0 (anchor): 3 bits = 4
        bw.Write(4, 3);
        // Pixels 1-15: 4 bits each = 0 (leave as zeros)

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("Mode6Interp(0,0)", bc7, 0, 0, expR, expG, expB, expA);

            // Pixel 1 has index 0, weight=0 -> selects ep0 exactly
            AssertBC7Pixel("Mode6Interp(1,0)", bc7, 1, 0, r0, g0, b0, a0);

            CompareAllPixelsWithUnity("Mode6Interp", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 1 partition test: different colors per subset ----
    //
    // Partition 13 for 2-subset: pixels 0-7 = subset 0, pixels 8-15 = subset 1
    // Subset 0: R=40, G=20, B=10 (6-bit endpoints)
    // Subset 1: R=60, G=30, B=15
    // Shared pbits: pbit0=0 (subset 0), pbit1=0 (subset 1)
    //
    // After pbit:
    // Subset 0: R=80, G=40, B=20 (7-bit)
    // Subset 1: R=120, G=60, B=30
    //
    // Unquantize(80,7)=161, Unquantize(40,7)=80, Unquantize(20,7)=40
    // Unquantize(120,7)=241, Unquantize(60,7)=120, Unquantize(30,7)=60
    //
    // Pixel (0,0) = index 0 = subset 0: R=161, G=80, B=40, A=255
    // Pixel (0,2) = index 8 = subset 1: R=241, G=120, B=60, A=255

    [TestInfo("BC7_Mode1_Partition")]
    public void TestMode1Partition()
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00000010, 2); // mode 1
        w.Write(13, 6); // partition 13

        // R endpoints: s0e0, s0e1, s1e0, s1e1
        w.Write(40, 6); // s0e0
        w.Write(40, 6); // s0e1
        w.Write(60, 6); // s1e0
        w.Write(60, 6); // s1e1

        // G endpoints
        w.Write(20, 6);
        w.Write(20, 6);
        w.Write(30, 6);
        w.Write(30, 6);

        // B endpoints
        w.Write(10, 6);
        w.Write(10, 6);
        w.Write(15, 6);
        w.Write(15, 6);

        // Shared pbits
        w.Write(0, 1); // pbit0 for subset 0
        w.Write(0, 1); // pbit1 for subset 1
        // indices all 0

        var (bc7, data) = MakeBC7(block);
        try
        {
            // Pixel (0,0) = pixel index 0 = subset 0
            AssertBC7Pixel("Part13_sub0", bc7, 0, 0, 161, 80, 40, 255);
            // Pixel (0,2) = pixel index 8 = subset 1
            AssertBC7Pixel("Part13_sub1", bc7, 0, 2, 241, 120, 60, 255);

            CompareAllPixelsWithUnity("Mode1Part", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 0 partition test: 3 subsets with different colors ----
    //
    // Partition 0 for 3-subset table:
    //   0,0,1,1, 0,0,1,1, 0,2,2,1, 2,2,2,2
    // So pixel 0 = subset 0, pixel 2 = subset 1, pixel 9 = subset 2

    [TestInfo("BC7_Mode0_Partition")]
    public void TestMode0Partition()
    {
        // Partition 0, 3-subset table row 0: 0,0,1,1,0,0,1,1,0,2,2,1,2,2,2,2
        // Pixel 0 -> subset 0, Pixel 2 -> subset 1, Pixel 9 -> subset 2

        int rS0 = 15,
            gS0 = 8,
            bS0 = 4; // subset 0 endpoints (4-bit)
        int rS1 = 10,
            gS1 = 5,
            bS1 = 2; // subset 1
        int rS2 = 5,
            gS2 = 3,
            bS2 = 1; // subset 2
        int pbit = 0;

        var block = new byte[16];
        var bw = new BitWriter(block);
        bw.Write(0b00000001, 1); // mode 0
        bw.Write(0, 4); // partition 0

        // R endpoints: s0e0, s0e1, s1e0, s1e1, s2e0, s2e1
        bw.Write(rS0, 4);
        bw.Write(rS0, 4);
        bw.Write(rS1, 4);
        bw.Write(rS1, 4);
        bw.Write(rS2, 4);
        bw.Write(rS2, 4);
        // G
        bw.Write(gS0, 4);
        bw.Write(gS0, 4);
        bw.Write(gS1, 4);
        bw.Write(gS1, 4);
        bw.Write(gS2, 4);
        bw.Write(gS2, 4);
        // B
        bw.Write(bS0, 4);
        bw.Write(bS0, 4);
        bw.Write(bS1, 4);
        bw.Write(bS1, 4);
        bw.Write(bS2, 4);
        bw.Write(bS2, 4);
        // pbits all 0
        bw.WriteN(0, 1, 6);
        // indices all 0

        // Compute expected via Unquantize (4-bit + pbit=0 -> 5-bit values)
        int exR0 = Unquantize((rS0 << 1) | pbit, 5);
        int exG0 = Unquantize((gS0 << 1) | pbit, 5);
        int exB0 = Unquantize((bS0 << 1) | pbit, 5);
        int exR1 = Unquantize((rS1 << 1) | pbit, 5);
        int exG1 = Unquantize((gS1 << 1) | pbit, 5);
        int exB1 = Unquantize((bS1 << 1) | pbit, 5);
        int exR2 = Unquantize((rS2 << 1) | pbit, 5);
        int exG2 = Unquantize((gS2 << 1) | pbit, 5);
        int exB2 = Unquantize((bS2 << 1) | pbit, 5);

        var (bc7, data) = MakeBC7(block);
        try
        {
            // Pixel (0,0) = index 0 -> subset 0
            AssertBC7Pixel("Part0_sub0", bc7, 0, 0, exR0, exG0, exB0, 255);
            // Pixel (2,0) = index 2 -> subset 1
            AssertBC7Pixel("Part0_sub1", bc7, 2, 0, exR1, exG1, exB1, 255);
            // Pixel (1,2) = index 9 -> subset 2
            AssertBC7Pixel("Part0_sub2", bc7, 1, 2, exR2, exG2, exB2, 255);

            CompareAllPixelsWithUnity("Mode0Part", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 5 rotation tests ----
    //
    // Base: R0=R1=100, G0=G1=50, B0=B1=25, A0=A1=200, rotation=0
    // Unquantize(100,7)=201, Unquantize(50,7)=100, Unquantize(25,7)=50, Unquantize(200,8)=200
    // rotation=0: R=201, G=100, B=50,  A=200
    // rotation=1: swap R<->A -> R=200, G=100, B=50,  A=201
    // rotation=2: swap G<->A -> R=201, G=200, B=50,  A=100
    // rotation=3: swap B<->A -> R=201, G=100, B=200, A=50

    byte[] BuildMode5Block(int r, int g, int b, int a, int rotation)
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00100000, 6); // mode 5
        w.Write(rotation, 2);
        w.WriteN(r, 7, 2); // R0, R1
        w.WriteN(g, 7, 2); // G0, G1
        w.WriteN(b, 7, 2); // B0, B1
        w.WriteN(a, 8, 2); // A0, A1
        return block;
    }

    [TestInfo("BC7_Mode5_Rotation")]
    public void TestMode5Rotation()
    {
        int baseR = Unquantize(100, 7); // 201
        int baseG = Unquantize(50, 7); // 100
        int baseB = Unquantize(25, 7); // 50
        int baseA = 200; // Unquantize(200,8) = 200

        var blk0 = BuildMode5Block(100, 50, 25, 200, 0);
        var blk1 = BuildMode5Block(100, 50, 25, 200, 1);
        var blk2 = BuildMode5Block(100, 50, 25, 200, 2);
        var blk3 = BuildMode5Block(100, 50, 25, 200, 3);

        var (bc7_0, d0) = MakeBC7(blk0);
        var (bc7_1, d1) = MakeBC7(blk1);
        var (bc7_2, d2) = MakeBC7(blk2);
        var (bc7_3, d3) = MakeBC7(blk3);

        try
        {
            AssertBC7Pixel("Rot0", bc7_0, 0, 0, baseR, baseG, baseB, baseA);
            AssertBC7Pixel("Rot1", bc7_1, 0, 0, baseA, baseG, baseB, baseR);
            AssertBC7Pixel("Rot2", bc7_2, 0, 0, baseR, baseA, baseB, baseG);
            AssertBC7Pixel("Rot3", bc7_3, 0, 0, baseR, baseG, baseA, baseB);

            CompareAllPixelsWithUnity("M5Rot0", blk0);
            CompareAllPixelsWithUnity("M5Rot1", blk1);
            CompareAllPixelsWithUnity("M5Rot2", blk2);
            CompareAllPixelsWithUnity("M5Rot3", blk3);
        }
        finally
        {
            d0.Dispose();
            d1.Dispose();
            d2.Dispose();
            d3.Dispose();
        }
    }

    // ---- Mode 4 rotation test (same logic as mode 5 but with separate index widths) ----

    byte[] BuildMode4Block(int r, int g, int b, int a, int rotation, int idxMode)
    {
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00010000, 5); // mode 4
        w.Write(rotation, 2);
        w.Write(idxMode, 1);
        w.WriteN(r, 5, 2); // R0, R1
        w.WriteN(g, 5, 2); // G0, G1
        w.WriteN(b, 5, 2); // B0, B1
        w.WriteN(a, 6, 2); // A0, A1
        return block;
    }

    [TestInfo("BC7_Mode4_Rotation")]
    public void TestMode4Rotation()
    {
        int baseR = Unquantize(20, 5); // 165
        int baseG = Unquantize(10, 5); // 82
        int baseB = Unquantize(5, 5); // 41
        int baseA = Unquantize(40, 6); // 162

        var blk0 = BuildMode4Block(20, 10, 5, 40, 0, 0);
        var blk1 = BuildMode4Block(20, 10, 5, 40, 1, 0);
        var blk2 = BuildMode4Block(20, 10, 5, 40, 2, 0);
        var blk3 = BuildMode4Block(20, 10, 5, 40, 3, 0);

        var (bc7_0, d0) = MakeBC7(blk0);
        var (bc7_1, d1) = MakeBC7(blk1);
        var (bc7_2, d2) = MakeBC7(blk2);
        var (bc7_3, d3) = MakeBC7(blk3);

        try
        {
            AssertBC7Pixel("M4Rot0", bc7_0, 0, 0, baseR, baseG, baseB, baseA);
            AssertBC7Pixel("M4Rot1", bc7_1, 0, 0, baseA, baseG, baseB, baseR);
            AssertBC7Pixel("M4Rot2", bc7_2, 0, 0, baseR, baseA, baseB, baseG);
            AssertBC7Pixel("M4Rot3", bc7_3, 0, 0, baseR, baseG, baseA, baseB);

            CompareAllPixelsWithUnity("M4Rot0", blk0);
            CompareAllPixelsWithUnity("M4Rot1", blk1);
            CompareAllPixelsWithUnity("M4Rot2", blk2);
            CompareAllPixelsWithUnity("M4Rot3", blk3);
        }
        finally
        {
            d0.Dispose();
            d1.Dispose();
            d2.Dispose();
            d3.Dispose();
        }
    }

    // ---- Mode 4 index mode test ----
    //
    // With idxMode=0: color uses 2-bit indices, alpha uses 3-bit indices
    // With idxMode=1: color uses 3-bit indices, alpha uses 2-bit indices
    //
    // Using different R endpoints (10 vs 30) and different A endpoints (20 vs 50)
    // with non-zero anchor index to show the weight difference.
    //
    // For pixel 0 (anchor):
    //   2-bit anchor index has 1 bit (0-1), 3-bit anchor index has 2 bits (0-3)
    //   Use idx2=1, idx3=2
    //
    // idxMode=0: colorIdx=idx2=1 -> w=Weights2[1]=21, alphaIdx=idx3=2 -> w=Weights3[2]=18
    // idxMode=1: colorIdx=idx3=2 -> w=Weights3[2]=18, alphaIdx=idx2=1 -> w=Weights2[1]=21

    [TestInfo("BC7_Mode4_IdxMode")]
    public void TestMode4IdxMode()
    {
        int rVal0 = 10,
            rVal1 = 30; // different R endpoints
        int gVal = 15; // same G
        int bVal = 5; // same B
        int aVal0 = 20,
            aVal1 = 50; // different A endpoints

        int rU0 = Unquantize(rVal0, 5);
        int rU1 = Unquantize(rVal1, 5);
        int gU = Unquantize(gVal, 5);
        int bU = Unquantize(bVal, 5);
        int aU0 = Unquantize(aVal0, 6);
        int aU1 = Unquantize(aVal1, 6);

        // idxMode=0: color uses 2-bit weights, alpha uses 3-bit weights
        {
            var block = new byte[16];
            var w = new BitWriter(block);
            w.Write(0b00010000, 5); // mode 4
            w.Write(0, 2); // rotation 0
            w.Write(0, 1); // idxMode 0
            w.Write(rVal0, 5);
            w.Write(rVal1, 5);
            w.Write(gVal, 5);
            w.Write(gVal, 5);
            w.Write(bVal, 5);
            w.Write(bVal, 5);
            w.Write(aVal0, 6);
            w.Write(aVal1, 6);
            // 2-bit indices: pixel 0 anchor = 1 bit, write value 1
            w.Write(1, 1);
            // pixels 1-15: 2 bits each = 0 (leave zeros)
            w.WriteN(0, 2, 15);
            // 3-bit indices: pixel 0 anchor = 2 bits, write value 2
            w.Write(2, 2);
            // pixels 1-15: 3 bits each = 0

            int colorW = Weights2[1]; // 21
            int alphaW = Weights3[2]; // 18
            int expR = Interpolate(rU0, rU1, colorW);
            int expG = Interpolate(gU, gU, colorW);
            int expB = Interpolate(bU, bU, colorW);
            int expA = Interpolate(aU0, aU1, alphaW);

            var (bc7, data) = MakeBC7(block);
            try
            {
                AssertBC7Pixel("IdxMode0", bc7, 0, 0, expR, expG, expB, expA);
                CompareAllPixelsWithUnity("IdxMode0", block);
            }
            finally
            {
                data.Dispose();
            }
        }

        // idxMode=1: color uses 3-bit weights, alpha uses 2-bit weights
        {
            var block = new byte[16];
            var w = new BitWriter(block);
            w.Write(0b00010000, 5); // mode 4
            w.Write(0, 2); // rotation 0
            w.Write(1, 1); // idxMode 1
            w.Write(rVal0, 5);
            w.Write(rVal1, 5);
            w.Write(gVal, 5);
            w.Write(gVal, 5);
            w.Write(bVal, 5);
            w.Write(bVal, 5);
            w.Write(aVal0, 6);
            w.Write(aVal1, 6);
            // 2-bit indices: pixel 0 anchor = 1 bit, write value 1
            w.Write(1, 1);
            w.WriteN(0, 2, 15);
            // 3-bit indices: pixel 0 anchor = 2 bits, write value 2
            w.Write(2, 2);

            // Now idxMode=1 swaps: colorIdx=idx3=2, alphaIdx=idx2=1
            int colorW = Weights3[2]; // 18
            int alphaW = Weights2[1]; // 21
            int expR = Interpolate(rU0, rU1, colorW);
            int expG = Interpolate(gU, gU, colorW);
            int expB = Interpolate(bU, bU, colorW);
            int expA = Interpolate(aU0, aU1, alphaW);

            var (bc7, data) = MakeBC7(block);
            try
            {
                AssertBC7Pixel("IdxMode1", bc7, 0, 0, expR, expG, expB, expA);
                CompareAllPixelsWithUnity("IdxMode1", block);
            }
            finally
            {
                data.Dispose();
            }
        }
    }

    // ---- Mode 6 p-bit test: verify p-bits correctly affect all channels ----
    //
    // Same 7-bit endpoint values but different p-bits:
    // pbit=0: R=(50<<1)|0=100, pbit=1: R=(50<<1)|1=101
    // Both 8-bit, no unquantize needed.

    [TestInfo("BC7_Mode6_PBits")]
    public void TestMode6PBits()
    {
        // pbit0=0, pbit1=0 -> ep0=100, ep1=100
        {
            var block = new byte[16];
            var w = new BitWriter(block);
            w.Write(0b01000000, 7);
            w.WriteN(50, 7, 2); // R
            w.WriteN(50, 7, 2); // G
            w.WriteN(50, 7, 2); // B
            w.WriteN(50, 7, 2); // A
            w.Write(0, 1); // pbit0=0
            w.Write(0, 1); // pbit1=0

            var (bc7, data) = MakeBC7(block);
            try
            {
                AssertBC7Pixel("PBit00", bc7, 0, 0, 100, 100, 100, 100);
                CompareAllPixelsWithUnity("PBit00", block);
            }
            finally
            {
                data.Dispose();
            }
        }

        // pbit0=1, pbit1=1 -> ep0=101, ep1=101
        {
            var block = new byte[16];
            var w = new BitWriter(block);
            w.Write(0b01000000, 7);
            w.WriteN(50, 7, 2);
            w.WriteN(50, 7, 2);
            w.WriteN(50, 7, 2);
            w.WriteN(50, 7, 2);
            w.Write(1, 1); // pbit0=1
            w.Write(1, 1); // pbit1=1

            var (bc7, data) = MakeBC7(block);
            try
            {
                AssertBC7Pixel("PBit11", bc7, 0, 0, 101, 101, 101, 101);
                CompareAllPixelsWithUnity("PBit11", block);
            }
            finally
            {
                data.Dispose();
            }
        }

        // pbit0=0, pbit1=1: ep0=100, ep1=101
        // With anchor index=7 -> weight=Weights4[7]=30
        // Interpolate(100, 101, 30) = (100*34 + 101*30 + 32)>>6 = (3400+3030+32)>>6 = 6462>>6 = 100
        {
            var block = new byte[16];
            var w = new BitWriter(block);
            w.Write(0b01000000, 7);
            w.WriteN(50, 7, 2);
            w.WriteN(50, 7, 2);
            w.WriteN(50, 7, 2);
            w.WriteN(50, 7, 2);
            w.Write(0, 1); // pbit0=0
            w.Write(1, 1); // pbit1=1
            w.Write(7, 3); // pixel 0 anchor index = 7

            int exp = Interpolate(100, 101, Weights4[7]);
            var (bc7, data) = MakeBC7(block);
            try
            {
                AssertBC7Pixel("PBit01_interp", bc7, 0, 0, exp, exp, exp, exp);
                CompareAllPixelsWithUnity("PBit01", block);
            }
            finally
            {
                data.Dispose();
            }
        }
    }

    // ---- Mode 1 shared p-bit test ----
    //
    // Mode 1 has SHARED p-bits: pbit0 applies to BOTH endpoints of subset 0,
    // pbit1 applies to BOTH endpoints of subset 1.
    // This is different from unique p-bits (modes 0, 3, 6, 7).

    [TestInfo("BC7_Mode1_SharedPBit")]
    public void TestMode1SharedPBit()
    {
        // Partition 0, all same endpoints R=40, G=20, B=10
        // pbit0=1 (subset 0): (40<<1)|1=81, Unquantize(81,7)=(81<<1)|(162>>7)=162|1=163
        // pbit1=0 (subset 1): (40<<1)|0=80, Unquantize(80,7)=(80<<1)|(160>>7)=160|1=161
        // For partition 0, pixel 0 is subset 0, so we get the pbit0 values.
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00000010, 2); // mode 1
        w.Write(0, 6); // partition 0
        w.WriteN(40, 6, 4); // R
        w.WriteN(20, 6, 4); // G
        w.WriteN(10, 6, 4); // B
        w.Write(1, 1); // pbit0 = 1 (shared for subset 0)
        w.Write(0, 1); // pbit1 = 0 (shared for subset 1)

        int rExp = Unquantize((40 << 1) | 1, 7); // 163
        int gExp = Unquantize((20 << 1) | 1, 7); // 82->(82>>7)=0->82
        int bExp = Unquantize((10 << 1) | 1, 7); // 42->(42>>7)=0->42

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("SharedPBit1", bc7, 0, 0, rExp, gExp, bExp, 255);
            CompareAllPixelsWithUnity("SharedPBit", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Non-anchor pixel interpolation test (Mode 6) ----
    //
    // Tests that non-anchor pixels correctly use 4-bit indices (not 3-bit).
    // Pixel 0 (anchor) gets 3 bits, pixels 1-15 get 4 bits.
    // We set pixel 1's index to 15 (max 4-bit), which would be 7 if misread as 3-bit.

    [TestInfo("BC7_Mode6_NonAnchorIndex")]
    public void TestMode6NonAnchorIndex()
    {
        int r0v = 50,
            r1v = 100;
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b01000000, 7); // mode 6
        w.Write(r0v, 7); // R0
        w.Write(r1v, 7); // R1
        w.WriteN(0, 7, 2); // G0, G1 = 0
        w.WriteN(0, 7, 2); // B0, B1 = 0
        w.WriteN(127, 7, 2); // A0, A1 = 127 (both same)
        w.Write(0, 1); // pbit0
        w.Write(0, 1); // pbit1
        // Pixel 0 (anchor): 3 bits = 0
        w.Write(0, 3);
        // Pixel 1: 4 bits = 15 (max index)
        w.Write(15, 4);
        // Rest = 0

        int re0 = (r0v << 1) | 0; // 100
        int re1 = (r1v << 1) | 0; // 200
        int ae = (127 << 1) | 0; // 254 for ep0, same for ep1

        var (bc7, data) = MakeBC7(block);
        try
        {
            // Pixel 0: index 0, weight=0 -> R=100
            AssertBC7Pixel("NonAnchor_p0", bc7, 0, 0, re0, 0, 0, ae);

            // Pixel 1: index 15, weight=Weights4[15]=64 -> R = Interpolate(100,200,64) = 200
            int expR1 = Interpolate(re0, re1, Weights4[15]);
            AssertBC7Pixel("NonAnchor_p1", bc7, 1, 0, expR1, 0, 0, ae);

            CompareAllPixelsWithUnity("NonAnchor", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 3 unique p-bit test ----
    //
    // Mode 3 has 4 unique p-bits: one per endpoint (s0e0, s0e1, s1e0, s1e1).
    // Each p-bit applies to ALL channels of that endpoint.

    [TestInfo("BC7_Mode3_UniquePBits")]
    public void TestMode3UniquePBits()
    {
        // Use partition 0, R=100 for all endpoints
        // pbit0=0 -> s0e0=(100<<1)|0=200
        // pbit1=1 -> s0e1=(100<<1)|1=201
        // pbit2=0, pbit3=1 (for subset 1)
        // All 8-bit, no unquantize needed.
        // With all indices=0, pixel 0 -> subset 0 ep0: R=200
        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b00001000, 4); // mode 3
        w.Write(0, 6); // partition 0
        w.WriteN(100, 7, 4); // R: all = 100
        w.WriteN(50, 7, 4); // G
        w.WriteN(25, 7, 4); // B
        w.Write(0, 1); // pbit0 for s0e0
        w.Write(1, 1); // pbit1 for s0e1
        w.Write(0, 1); // pbit2 for s1e0
        w.Write(1, 1); // pbit3 for s1e1
        // indices all 0

        // Pixel 0 -> subset 0, index 0 -> ep0
        // ep0 uses pbit0=0: R=(100<<1)|0=200, G=(50<<1)|0=100, B=(25<<1)|0=50
        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("UniqPBit_ep0", bc7, 0, 0, 200, 100, 50, 255);
            CompareAllPixelsWithUnity("UniqPBit", block);
        }
        finally
        {
            data.Dispose();
        }
    }

    // ---- Mode 7 partition + alpha test ----
    //
    // Mode 7 includes alpha (RGBA with 5-bit endpoints + pbit = 6-bit).
    // Uses partition 13 (same as Mode 1 test): pixels 0-7 = subset 0, 8-15 = subset 1.

    [TestInfo("BC7_Mode7_PartitionAlpha")]
    public void TestMode7PartitionAlpha()
    {
        int rS0 = 20,
            gS0 = 10,
            bS0 = 5,
            aS0 = 25;
        int rS1 = 30,
            gS1 = 15,
            bS1 = 8,
            aS1 = 20;

        var block = new byte[16];
        var w = new BitWriter(block);
        w.Write(0b10000000, 8); // mode 7
        w.Write(13, 6); // partition 13

        // R: s0e0, s0e1, s1e0, s1e1
        w.Write(rS0, 5);
        w.Write(rS0, 5);
        w.Write(rS1, 5);
        w.Write(rS1, 5);
        // G
        w.Write(gS0, 5);
        w.Write(gS0, 5);
        w.Write(gS1, 5);
        w.Write(gS1, 5);
        // B
        w.Write(bS0, 5);
        w.Write(bS0, 5);
        w.Write(bS1, 5);
        w.Write(bS1, 5);
        // A
        w.Write(aS0, 5);
        w.Write(aS0, 5);
        w.Write(aS1, 5);
        w.Write(aS1, 5);
        // pbits all 0
        w.WriteN(0, 1, 4);
        // indices all 0

        // Subset 0 (pixel 0): 5-bit + pbit=0 -> 6-bit
        int exR0 = Unquantize(rS0 << 1, 6);
        int exG0 = Unquantize(gS0 << 1, 6);
        int exB0 = Unquantize(bS0 << 1, 6);
        int exA0 = Unquantize(aS0 << 1, 6);

        // Subset 1 (pixel 8 = (0,2)): same calculation
        int exR1 = Unquantize(rS1 << 1, 6);
        int exG1 = Unquantize(gS1 << 1, 6);
        int exB1 = Unquantize(bS1 << 1, 6);
        int exA1 = Unquantize(aS1 << 1, 6);

        var (bc7, data) = MakeBC7(block);
        try
        {
            AssertBC7Pixel("M7Part_sub0", bc7, 0, 0, exR0, exG0, exB0, exA0);
            AssertBC7Pixel("M7Part_sub1", bc7, 0, 2, exR1, exG1, exB1, exA1);
            CompareAllPixelsWithUnity("M7Part", block);
        }
        finally
        {
            data.Dispose();
        }
    }
}
