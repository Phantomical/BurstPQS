using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC6H compressed format. Encodes HDR RGB (no alpha) in 16-byte blocks covering
    /// 4x4 pixels (8 bits/pixel). Supports 14 modes (0-13) with 1 or 2 subsets,
    /// half-float endpoints (10-16 bits) with optional delta encoding, and 3- or 4-bit
    /// indices. Endpoints are unquantized to 16-bit half-precision floats and interpolated.
    /// Comes in signed (<c>BC6H_SF16</c>) and unsigned (<c>BC6H_UF16</c>) variants.
    /// </summary>
    [BurstCompile]
    public struct BC6H : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;
        bool signed;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public BC6H(Texture2D texture, MapSO.MapDepth depth, bool signed = false)
        {
            ValidateFormat(texture, TextureFormat.BC6H);
            this = new BC6H(
                texture.GetRawTextureData<byte>(),
                texture.width,
                texture.height,
                depth,
                signed
            );
        }

        public BC6H(
            NativeArray<byte> data,
            int width,
            int height,
            MapSO.MapDepth depth,
            bool signed = false
        )
        {
            int required = ((width + 3) / 4) * ((height + 3) / 4) * 16;
            if (data.Length < required)
                throw new ArgumentException(
                    $"Data length {data.Length} is too small for {width}x{height} BC6H texture (need at least {required})",
                    nameof(data)
                );
            this.data = data;
            Width = width;
            Height = height;
            this.depth = depth;
            this.signed = signed;
        }

        readonly void GetComponents(int x, int y, out float r, out float g, out float b)
        {
            x = math.clamp(x, 0, Width - 1);
            y = math.clamp(y, 0, Height - 1);
            BlockCoords(x, y, Width, out int blockOffset, out int lx, out int ly, 16);
            DecodeBC6HPixel(data, blockOffset, lx, ly, signed, out r, out g, out b);
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToFloat(r, g, b, 1f, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToColor(r, g, b, 1f, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToColor32(r, g, b, 1f, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b);
            return DepthToHeightAlpha(r, g, b, 1f, depth);
        }

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

    // BC6H mode descriptors: { numSubsets, partitionBits, endpointBits, deltaBitsR, deltaBitsG, deltaBitsB, indexBits }
    // Modes 0-9 (two-region), 10-13 (one-region)
    struct BC6HModeInfo
    {
        public int numSubsets;
        public int partitionBits;
        public int endpointBits; // base endpoint precision
        public int3 deltaBits; // delta bits for R,G,B (0 if no deltas i.e. direct mode)
        public bool transformed; // whether endpoints are delta-encoded
        public int indexBits;
    }

    static readonly BC6HModeInfo[] BC6HModes =
    {
        // Mode 0: 2 subsets, 10-bit base, 5/5/5 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 10,
            deltaBits = new int3(5, 5, 5),
            transformed = true,
            indexBits = 3,
        },
        // Mode 1: 2 subsets, 7-bit base, 6/6/6 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 7,
            deltaBits = new int3(6, 6, 6),
            transformed = true,
            indexBits = 3,
        },
        // Mode 2: 2 subsets, 11-bit base (10+1), 5/4/4 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 11,
            deltaBits = new int3(5, 4, 4),
            transformed = true,
            indexBits = 3,
        },
        // Mode 3: 2 subsets, 11-bit base, 4/5/4 delta (swizzled)
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 11,
            deltaBits = new int3(4, 5, 4),
            transformed = true,
            indexBits = 3,
        },
        // Mode 4: 2 subsets, 11-bit base, 4/4/5 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 11,
            deltaBits = new int3(4, 4, 5),
            transformed = true,
            indexBits = 3,
        },
        // Mode 5: 2 subsets, 9-bit base, 5/5/5 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 9,
            deltaBits = new int3(5, 5, 5),
            transformed = true,
            indexBits = 3,
        },
        // Mode 6: 2 subsets, 8-bit base, 6/5/5 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 8,
            deltaBits = new int3(6, 5, 5),
            transformed = true,
            indexBits = 3,
        },
        // Mode 7: 2 subsets, 8-bit base, 5/6/5 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 8,
            deltaBits = new int3(5, 6, 5),
            transformed = true,
            indexBits = 3,
        },
        // Mode 8: 2 subsets, 8-bit base, 5/5/6 delta
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 8,
            deltaBits = new int3(5, 5, 6),
            transformed = true,
            indexBits = 3,
        },
        // Mode 9: 2 subsets, 6-bit base, 6/6/6 delta (no transform)
        new()
        {
            numSubsets = 2,
            partitionBits = 5,
            endpointBits = 6,
            deltaBits = new int3(6, 6, 6),
            transformed = false,
            indexBits = 3,
        },
        // Mode 10: 1 subset, 10-bit base, 10/10/10 direct
        new()
        {
            numSubsets = 1,
            partitionBits = 0,
            endpointBits = 10,
            deltaBits = new int3(10, 10, 10),
            transformed = false,
            indexBits = 4,
        },
        // Mode 11: 1 subset, 11-bit base, 9/9/9 delta
        new()
        {
            numSubsets = 1,
            partitionBits = 0,
            endpointBits = 11,
            deltaBits = new int3(9, 9, 9),
            transformed = true,
            indexBits = 4,
        },
        // Mode 12: 1 subset, 12-bit base, 8/8/8 delta
        new()
        {
            numSubsets = 1,
            partitionBits = 0,
            endpointBits = 12,
            deltaBits = new int3(8, 8, 8),
            transformed = true,
            indexBits = 4,
        },
        // Mode 13: 1 subset, 16-bit base, 4/4/4 delta
        new()
        {
            numSubsets = 1,
            partitionBits = 0,
            endpointBits = 16,
            deltaBits = new int3(4, 4, 4),
            transformed = true,
            indexBits = 4,
        },
    };

    // csharpier-ignore-start

    // BC6H uses the same 2-subset partition table as BC7 (first 32 entries)
    static readonly byte[] BC6HPartitionTable =
    {
        0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, // partition 0
        0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, // partition 1
        0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, // partition 2
        0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 1, // partition 3
        0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1, // partition 4
        0, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, // partition 5
        0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, // partition 6
        0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, // partition 7
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, // partition 8
        0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // partition 9
        0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, // partition 10
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, // partition 11
        0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // partition 12
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, // partition 13
        0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // partition 14
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, // partition 15
        0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1, // partition 16
        0, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, // partition 17
        0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, // partition 18
        0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, // partition 19
        0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, // partition 20
        0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, // partition 21
        0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, // partition 22
        0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, // partition 23
        0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, // partition 24
        0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, // partition 25
        0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, // partition 26
        0, 0, 1, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0, // partition 27
        0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0, // partition 28
        0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, // partition 29
        0, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0, // partition 30
        0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, // partition 31
    };

    static readonly byte[] BC6HAnchorIndex =
    {
        15, 15, 15, 15, 15, 15, 15, 15,
        15, 15, 15, 15, 15, 15, 15, 15,
        15,  2,  8,  2,  2,  8,  8, 15,
         2,  8,  2,  2,  8,  8,  2,  2,
    };
    // csharpier-ignore-end

    static int BC6HGetMode(NativeArray<byte> data, int offset)
    {
        int b0 = data[offset];
        int b1 = data[offset + 1];

        // Modes encoded in first 2-5 bits
        if ((b0 & 0x03) == 0x00)
            return 0; // 00
        if ((b0 & 0x03) == 0x01)
            return 1; // 01
        if ((b0 & 0x1F) == 0x02)
            return 2; // 00010
        if ((b0 & 0x1F) == 0x06)
            return 3; // 00110
        if ((b0 & 0x1F) == 0x0A)
            return 4; // 01010
        if ((b0 & 0x1F) == 0x0E)
            return 5; // 01110
        if ((b0 & 0x1F) == 0x12)
            return 6; // 10010
        if ((b0 & 0x1F) == 0x16)
            return 7; // 10110
        if ((b0 & 0x1F) == 0x1A)
            return 8; // 11010
        if ((b0 & 0x1F) == 0x1E)
            return 9; // 11110
        if ((b0 & 0x1F) == 0x03)
            return 10; // 00011
        if ((b0 & 0x1F) == 0x07)
            return 11; // 00111
        if ((b0 & 0x1F) == 0x0B)
            return 12; // 01011
        if ((b0 & 0x1F) == 0x0F)
            return 13; // 01111
        return -1; // reserved
    }

    static int SignExtend(int val, int bits)
    {
        int shift = 32 - bits;
        return (val << shift) >> shift;
    }

    static float HalfToFloat(int h)
    {
        // Convert a 16-bit half-float to float
        int sign = (h >> 15) & 1;
        int exp = (h >> 10) & 0x1F;
        int mantissa = h & 0x3FF;

        if (exp == 0)
        {
            if (mantissa == 0)
                return sign == 1 ? -0f : 0f;
            // Denormalized
            float f = mantissa / 1024f;
            f *= 1f / 16384f; // 2^-14
            return sign == 1 ? -f : f;
        }
        else if (exp == 31)
        {
            return mantissa == 0
                ? (sign == 1 ? float.NegativeInfinity : float.PositiveInfinity)
                : float.NaN;
        }
        else
        {
            float f = (1f + mantissa / 1024f) * math.pow(2f, exp - 15);
            return sign == 1 ? -f : f;
        }
    }

    static int UnquantizeBC6H(int val, int bits, bool signed)
    {
        if (signed)
        {
            if (bits >= 16)
                return val;
            bool s = false;
            if (val < 0)
            {
                s = true;
                val = -val;
            }
            int unq;
            if (val == 0)
                unq = 0;
            else if (val >= ((1 << (bits - 1)) - 1))
                unq = 0x7FFF;
            else
                unq = ((val << 15) + 0x4000) >> (bits - 1);
            return s ? -unq : unq;
        }
        else
        {
            if (bits >= 15)
                return val;
            if (val == 0)
                return 0;
            if (val == ((1 << bits) - 1))
                return 0xFFFF;
            return ((val << 15) + 0x4000) >> (bits - 1);
        }
    }

    static float FinishUnquantizeBC6H(int val, bool signed)
    {
        if (signed)
        {
            int s = 0;
            if (val < 0)
            {
                s = 0x8000;
                val = -val;
            }
            int h = s | ((val * 31) >> 5);
            return HalfToFloat(h);
        }
        else
        {
            int h = (val * 31) >> 6;
            return HalfToFloat(h);
        }
    }

    static void DecodeBC6HPixel(
        NativeArray<byte> data,
        int blockOffset,
        int localX,
        int localY,
        bool signed,
        out float r,
        out float g,
        out float b
    )
    {
        int mode = BC6HGetMode(data, blockOffset);
        if (mode < 0 || mode >= 14)
        {
            r = g = b = 0f;
            return;
        }

        var modeInfo = BC6HModes[mode];

        int3 e0 = 0,
            e1 = 0,
            e2 = 0,
            e3 = 0; // endpoints: [subset0_lo, subset0_hi, subset1_lo, subset1_hi]
        int partition = 0;

        DecodeBC6HEndpoints(
            data,
            blockOffset,
            mode,
            modeInfo,
            out e0,
            out e1,
            out e2,
            out e3,
            out partition
        );

        // Apply transforms (delta decoding)
        if (modeInfo.transformed)
        {
            // e1, e2, e3 are deltas from e0
            e1.x = SignExtend(e1.x, modeInfo.deltaBits.x) + e0.x;
            e1.y = SignExtend(e1.y, modeInfo.deltaBits.y) + e0.y;
            e1.z = SignExtend(e1.z, modeInfo.deltaBits.z) + e0.z;

            if (modeInfo.numSubsets == 2)
            {
                e2.x = SignExtend(e2.x, modeInfo.deltaBits.x) + e0.x;
                e2.y = SignExtend(e2.y, modeInfo.deltaBits.y) + e0.y;
                e2.z = SignExtend(e2.z, modeInfo.deltaBits.z) + e0.z;
                e3.x = SignExtend(e3.x, modeInfo.deltaBits.x) + e0.x;
                e3.y = SignExtend(e3.y, modeInfo.deltaBits.y) + e0.y;
                e3.z = SignExtend(e3.z, modeInfo.deltaBits.z) + e0.z;
            }

            // Mask to endpoint precision
            int mask = (1 << modeInfo.endpointBits) - 1;
            if (signed)
            {
                e0.x = SignExtend(e0.x & mask, modeInfo.endpointBits);
                e0.y = SignExtend(e0.y & mask, modeInfo.endpointBits);
                e0.z = SignExtend(e0.z & mask, modeInfo.endpointBits);
                e1.x = SignExtend(e1.x & mask, modeInfo.endpointBits);
                e1.y = SignExtend(e1.y & mask, modeInfo.endpointBits);
                e1.z = SignExtend(e1.z & mask, modeInfo.endpointBits);
                e2.x = SignExtend(e2.x & mask, modeInfo.endpointBits);
                e2.y = SignExtend(e2.y & mask, modeInfo.endpointBits);
                e2.z = SignExtend(e2.z & mask, modeInfo.endpointBits);
                e3.x = SignExtend(e3.x & mask, modeInfo.endpointBits);
                e3.y = SignExtend(e3.y & mask, modeInfo.endpointBits);
                e3.z = SignExtend(e3.z & mask, modeInfo.endpointBits);
            }
            else
            {
                e0 &= mask;
                e1 &= mask;
                e2 &= mask;
                e3 &= mask;
            }
        }

        // Unquantize endpoints
        e0.x = UnquantizeBC6H(e0.x, modeInfo.endpointBits, signed);
        e0.y = UnquantizeBC6H(e0.y, modeInfo.endpointBits, signed);
        e0.z = UnquantizeBC6H(e0.z, modeInfo.endpointBits, signed);
        e1.x = UnquantizeBC6H(e1.x, modeInfo.endpointBits, signed);
        e1.y = UnquantizeBC6H(e1.y, modeInfo.endpointBits, signed);
        e1.z = UnquantizeBC6H(e1.z, modeInfo.endpointBits, signed);
        e2.x = UnquantizeBC6H(e2.x, modeInfo.endpointBits, signed);
        e2.y = UnquantizeBC6H(e2.y, modeInfo.endpointBits, signed);
        e2.z = UnquantizeBC6H(e2.z, modeInfo.endpointBits, signed);
        e3.x = UnquantizeBC6H(e3.x, modeInfo.endpointBits, signed);
        e3.y = UnquantizeBC6H(e3.y, modeInfo.endpointBits, signed);
        e3.z = UnquantizeBC6H(e3.z, modeInfo.endpointBits, signed);

        // Read indices
        int pixelIndex = localY * 4 + localX;
        int subset;
        if (modeInfo.numSubsets == 2)
            subset = BC6HPartitionTable[partition * 16 + pixelIndex];
        else
            subset = 0;

        int anchor0 = 0;
        int anchor1 = modeInfo.numSubsets == 2 ? BC6HAnchorIndex[partition] : -1;

        // Compute bit offset for the index data
        // Index data starts after mode + partition + endpoint bits
        // For simplicity, we read from a known bit position based on BC6H spec:
        // Indices always occupy the last 46 bits (2-subset, 3-bit) or 63 bits (1-subset, 4-bit)
        int indexStart = 128 - (modeInfo.numSubsets == 2 ? 46 : 63);
        var idxReader = new BC7BitReader(data, blockOffset);
        idxReader.SkipBits(indexStart);

        int idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int bits = modeInfo.indexBits;
            if (i == anchor0 || i == anchor1)
                bits--;
            int val = idxReader.ReadBits(bits);
            if (i == pixelIndex)
                idx = val;
        }

        int3 lo,
            hi;
        if (subset == 0)
        {
            lo = e0;
            hi = e1;
        }
        else
        {
            lo = e2;
            hi = e3;
        }

        int w = modeInfo.indexBits == 3 ? BC7Weights3[idx] : BC7Weights4[idx];

        int finalR = ((64 - w) * lo.x + w * hi.x + 32) >> 6;
        int finalG = ((64 - w) * lo.y + w * hi.y + 32) >> 6;
        int finalB = ((64 - w) * lo.z + w * hi.z + 32) >> 6;

        r = FinishUnquantizeBC6H(finalR, signed);
        g = FinishUnquantizeBC6H(finalG, signed);
        b = FinishUnquantizeBC6H(finalB, signed);
    }

    // Reverse the bit order of a value read from the bitstream.
    // Used by BC6H modes 12 and 13 where high base endpoint bits are stored in reversed order.
    static int ReverseBits(int val, int numBits)
    {
        int result = 0;
        for (int i = 0; i < numBits; i++)
        {
            result = (result << 1) | (val & 1);
            val >>= 1;
        }
        return result;
    }

    // BC6H endpoint extraction using sequential bitstream reads per the BC6H specification.
    // Each mode has a unique bit layout where endpoint fields are interleaved in a specific order.
    // Bit sequences are from the bcdec reference decoder (https://github.com/iOrange/bcdec).
    // e0 = endpt[0].A (base), e1 = endpt[0].B, e2 = endpt[1].A, e3 = endpt[1].B
    static void DecodeBC6HEndpoints(
        NativeArray<byte> data,
        int offset,
        int mode,
        BC6HModeInfo modeInfo,
        out int3 e0,
        out int3 e1,
        out int3 e2,
        out int3 e3,
        out int partition
    )
    {
        e0 = e1 = e2 = e3 = 0;
        partition = 0;

        var r = new BC7BitReader(data, offset);

        // Skip mode bits
        if (mode <= 1)
            r.SkipBits(2);
        else
            r.SkipBits(5);

        switch (mode)
        {
            case 0: // 10-bit base, 5/5/5 delta
            {
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(5); // rx[4:0]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(5); // gx[4:0]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(5); // bx[4:0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(5); // ry[4:0]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e3.x |= r.ReadBits(5); // rz[4:0]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                partition = r.ReadBits(5);
                break;
            }

            case 1: // 7-bit base, 6/6/6 delta
            {
                e2.y |= r.ReadBits(1) << 5; // gy[5]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e3.y |= r.ReadBits(1) << 5; // gz[5]
                e0.x |= r.ReadBits(7); // rw[6:0]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e0.y |= r.ReadBits(7); // gw[6:0]
                e2.z |= r.ReadBits(1) << 5; // by[5]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e0.z |= r.ReadBits(7); // bw[6:0]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                e3.z |= r.ReadBits(1) << 5; // bz[5]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e1.x |= r.ReadBits(6); // rx[5:0]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(6); // gx[5:0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(6); // bx[5:0]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(6); // ry[5:0]
                e3.x |= r.ReadBits(6); // rz[5:0]
                partition = r.ReadBits(5);
                break;
            }

            case 2: // 11-bit base (10+1), 5/4/4 delta
            {
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(5); // rx[4:0]
                e0.x |= r.ReadBits(1) << 10; // rw[10]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(4); // gx[3:0]
                e0.y |= r.ReadBits(1) << 10; // gw[10]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(4); // bx[3:0]
                e0.z |= r.ReadBits(1) << 10; // bw[10]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(5); // ry[4:0]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e3.x |= r.ReadBits(5); // rz[4:0]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                partition = r.ReadBits(5);
                break;
            }

            case 3: // 11-bit base, 4/5/4 delta
            {
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(4); // rx[3:0]
                e0.x |= r.ReadBits(1) << 10; // rw[10]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(5); // gx[4:0]
                e0.y |= r.ReadBits(1) << 10; // gw[10]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(4); // bx[3:0]
                e0.z |= r.ReadBits(1) << 10; // bw[10]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(4); // ry[3:0]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e3.x |= r.ReadBits(4); // rz[3:0]
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                partition = r.ReadBits(5);
                break;
            }

            case 4: // 11-bit base, 4/4/5 delta
            {
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(4); // rx[3:0]
                e0.x |= r.ReadBits(1) << 10; // rw[10]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(4); // gx[3:0]
                e0.y |= r.ReadBits(1) << 10; // gw[10]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(5); // bx[4:0]
                e0.z |= r.ReadBits(1) << 10; // bw[10]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(4); // ry[3:0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e3.x |= r.ReadBits(4); // rz[3:0]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                partition = r.ReadBits(5);
                break;
            }

            case 5: // 9-bit base, 5/5/5 delta
            {
                e0.x |= r.ReadBits(9); // rw[8:0]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e0.y |= r.ReadBits(9); // gw[8:0]
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e0.z |= r.ReadBits(9); // bw[8:0]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e1.x |= r.ReadBits(5); // rx[4:0]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(5); // gx[4:0]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(5); // bx[4:0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(5); // ry[4:0]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e3.x |= r.ReadBits(5); // rz[4:0]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                partition = r.ReadBits(5);
                break;
            }

            case 6: // 8-bit base, 6/5/5 delta
            {
                e0.x |= r.ReadBits(8); // rw[7:0]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e0.y |= r.ReadBits(8); // gw[7:0]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e0.z |= r.ReadBits(8); // bw[7:0]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e1.x |= r.ReadBits(6); // rx[5:0]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(5); // gx[4:0]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(5); // bx[4:0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(6); // ry[5:0]
                e3.x |= r.ReadBits(6); // rz[5:0]
                partition = r.ReadBits(5);
                break;
            }

            case 7: // 8-bit base, 5/6/5 delta
            {
                e0.x |= r.ReadBits(8); // rw[7:0]
                e3.z |= r.ReadBits(1); // bz[0]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e0.y |= r.ReadBits(8); // gw[7:0]
                e2.y |= r.ReadBits(1) << 5; // gy[5]
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e0.z |= r.ReadBits(8); // bw[7:0]
                e3.y |= r.ReadBits(1) << 5; // gz[5]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e1.x |= r.ReadBits(5); // rx[4:0]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(6); // gx[5:0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(5); // bx[4:0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(5); // ry[4:0]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e3.x |= r.ReadBits(5); // rz[4:0]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                partition = r.ReadBits(5);
                break;
            }

            case 8: // 8-bit base, 5/5/6 delta
            {
                e0.x |= r.ReadBits(8); // rw[7:0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e0.y |= r.ReadBits(8); // gw[7:0]
                e2.z |= r.ReadBits(1) << 5; // by[5]
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e0.z |= r.ReadBits(8); // bw[7:0]
                e3.z |= r.ReadBits(1) << 5; // bz[5]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e1.x |= r.ReadBits(5); // rx[4:0]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(5); // gx[4:0]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(6); // bx[5:0]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(5); // ry[4:0]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e3.x |= r.ReadBits(5); // rz[4:0]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                partition = r.ReadBits(5);
                break;
            }

            case 9: // 6-bit base, 6/6/6 (no transform)
            {
                e0.x |= r.ReadBits(6); // rw[5:0]
                e3.y |= r.ReadBits(1) << 4; // gz[4]
                e3.z |= r.ReadBits(1); // bz[0]
                e3.z |= r.ReadBits(1) << 1; // bz[1]
                e2.z |= r.ReadBits(1) << 4; // by[4]
                e0.y |= r.ReadBits(6); // gw[5:0]
                e2.y |= r.ReadBits(1) << 5; // gy[5]
                e2.z |= r.ReadBits(1) << 5; // by[5]
                e3.z |= r.ReadBits(1) << 2; // bz[2]
                e2.y |= r.ReadBits(1) << 4; // gy[4]
                e0.z |= r.ReadBits(6); // bw[5:0]
                e3.y |= r.ReadBits(1) << 5; // gz[5]
                e3.z |= r.ReadBits(1) << 3; // bz[3]
                e3.z |= r.ReadBits(1) << 5; // bz[5]
                e3.z |= r.ReadBits(1) << 4; // bz[4]
                e1.x |= r.ReadBits(6); // rx[5:0]
                e2.y |= r.ReadBits(4); // gy[3:0]
                e1.y |= r.ReadBits(6); // gx[5:0]
                e3.y |= r.ReadBits(4); // gz[3:0]
                e1.z |= r.ReadBits(6); // bx[5:0]
                e2.z |= r.ReadBits(4); // by[3:0]
                e2.x |= r.ReadBits(6); // ry[5:0]
                e3.x |= r.ReadBits(6); // rz[5:0]
                partition = r.ReadBits(5);
                break;
            }

            case 10: // 10-bit direct, no delta, no transform
            {
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(10); // rx[9:0]
                e1.y |= r.ReadBits(10); // gx[9:0]
                e1.z |= r.ReadBits(10); // bx[9:0]
                break;
            }

            case 11: // 11-bit base (10+1), 9/9/9 delta
            {
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(9); // rx[8:0]
                e0.x |= r.ReadBits(1) << 10; // rw[10]
                e1.y |= r.ReadBits(9); // gx[8:0]
                e0.y |= r.ReadBits(1) << 10; // gw[10]
                e1.z |= r.ReadBits(9); // bx[8:0]
                e0.z |= r.ReadBits(1) << 10; // bw[10]
                break;
            }

            case 12: // 12-bit base (10+2 reversed), 8/8/8 delta
            {
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(8); // rx[7:0]
                e0.x |= ReverseBits(r.ReadBits(2), 2) << 10; // rw[11:10] reversed
                e1.y |= r.ReadBits(8); // gx[7:0]
                e0.y |= ReverseBits(r.ReadBits(2), 2) << 10; // gw[11:10] reversed
                e1.z |= r.ReadBits(8); // bx[7:0]
                e0.z |= ReverseBits(r.ReadBits(2), 2) << 10; // bw[11:10] reversed
                break;
            }

            case 13: // 16-bit base (10+6 reversed), 4/4/4 delta
            {
                e0.x |= r.ReadBits(10); // rw[9:0]
                e0.y |= r.ReadBits(10); // gw[9:0]
                e0.z |= r.ReadBits(10); // bw[9:0]
                e1.x |= r.ReadBits(4); // rx[3:0]
                e0.x |= ReverseBits(r.ReadBits(6), 6) << 10; // rw[15:10] reversed
                e1.y |= r.ReadBits(4); // gx[3:0]
                e0.y |= ReverseBits(r.ReadBits(6), 6) << 10; // gw[15:10] reversed
                e1.z |= r.ReadBits(4); // bx[3:0]
                e0.z |= ReverseBits(r.ReadBits(6), 6) << 10; // bw[15:10] reversed
                break;
            }
        }
    }
}
