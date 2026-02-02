using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

public static partial class TextureMapSO
{
    /// <summary>
    /// BC7 compressed format. Encodes RGBA in 16-byte blocks covering 4x4 pixels
    /// (8 bits/pixel). Supports 8 modes (0-7) with varying numbers of subsets (1-3),
    /// endpoint precision (4-8 bits), index precision (2-4 bits), and optional
    /// per-endpoint p-bits, channel rotation, and separate color/alpha index sets.
    /// Partition tables select which pixels belong to which subset.
    /// </summary>
    [BurstCompile]
    public struct BC7 : IMapSO
    {
        NativeArray<byte> data;
        MapSO.MapDepth depth;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public BC7(Texture2D texture, MapSO.MapDepth depth)
        {
            ValidateFormat(texture, TextureFormat.BC7);
            data = texture.GetRawTextureData<byte>();
            Width = texture.width;
            Height = texture.height;
            this.depth = depth;
        }

        readonly void GetComponents(
            int x,
            int y,
            out float r,
            out float g,
            out float b,
            out float a
        )
        {
            x = math.clamp(x, 0, Width - 1);
            y = math.clamp(y, 0, Height - 1);
            BlockCoords(x, y, Width, out int blockOffset, out int lx, out int ly, 16);
            DecodeBC7Pixel(data, blockOffset, lx, ly, out r, out g, out b, out a);
        }

        public readonly float GetPixelFloat(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b, out float a);
            return DepthToFloat(r, g, b, a, depth);
        }

        public readonly Color GetPixelColor(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b, out float a);
            return DepthToColor(r, g, b, a, depth);
        }

        public readonly Color32 GetPixelColor32(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b, out float a);
            return DepthToColor32(r, g, b, a, depth);
        }

        public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            GetComponents(x, y, out float r, out float g, out float b, out float a);
            return DepthToHeightAlpha(r, g, b, a, depth);
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

    // BC7 partition tables and decode logic

    // csharpier-ignore-start
    // 2-subset partition table: 64 partitions x 16 pixels
    static readonly byte[] BC7PartitionTable2 =
    {
        0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, // partition  0
        0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1, // partition  1
        0,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1, // partition  2
        0,0,0,1,0,0,1,1,0,0,1,1,0,1,1,1, // partition  3
        0,0,0,0,0,0,0,1,0,0,0,1,0,0,1,1, // partition  4
        0,0,1,1,0,1,1,1,0,1,1,1,1,1,1,1, // partition  5
        0,0,0,1,0,0,1,1,0,1,1,1,1,1,1,1, // partition  6
        0,0,0,0,0,0,0,1,0,0,1,1,0,1,1,1, // partition  7
        0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,1, // partition  8
        0,0,1,1,0,1,1,1,1,1,1,1,1,1,1,1, // partition  9
        0,0,0,0,0,0,0,1,0,1,1,1,1,1,1,1, // partition 10
        0,0,0,0,0,0,0,0,0,0,0,1,0,1,1,1, // partition 11
        0,0,0,1,0,1,1,1,1,1,1,1,1,1,1,1, // partition 12
        0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1, // partition 13
        0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1, // partition 14
        0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1, // partition 15
        0,0,0,0,1,0,0,0,1,1,1,0,1,1,1,1, // partition 16
        0,1,1,1,0,0,0,1,0,0,0,0,0,0,0,0, // partition 17
        0,0,0,0,0,0,0,0,1,0,0,0,1,1,1,0, // partition 18
        0,1,1,1,0,0,1,1,0,0,0,1,0,0,0,0, // partition 19
        0,0,1,1,0,0,0,1,0,0,0,0,0,0,0,0, // partition 20
        0,0,0,0,1,0,0,0,1,1,0,0,1,1,1,0, // partition 21
        0,0,0,0,0,0,0,0,1,0,0,0,1,1,0,0, // partition 22
        0,1,1,1,0,0,1,1,0,0,1,1,0,0,0,1, // partition 23
        0,0,1,1,0,0,0,1,0,0,0,1,0,0,0,0, // partition 24
        0,0,0,0,1,0,0,0,1,0,0,0,1,1,0,0, // partition 25
        0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0, // partition 26
        0,0,1,1,0,1,1,0,0,1,1,0,1,1,0,0, // partition 27
        0,0,0,1,0,1,1,1,1,1,1,0,1,0,0,0, // partition 28
        0,0,0,0,1,1,1,1,1,1,1,1,0,0,0,0, // partition 29
        0,1,1,1,0,0,0,1,1,0,0,0,1,1,1,0, // partition 30
        0,0,1,1,1,0,0,1,1,0,0,1,1,1,0,0, // partition 31
        0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1, // partition 32
        0,0,0,0,1,1,1,1,0,0,0,0,1,1,1,1, // partition 33
        0,1,0,1,1,0,1,0,0,1,0,1,1,0,1,0, // partition 34
        0,0,1,1,0,0,1,1,1,1,0,0,1,1,0,0, // partition 35
        0,0,1,1,1,1,0,0,0,0,1,1,1,1,0,0, // partition 36
        0,1,0,1,0,1,0,1,1,0,1,0,1,0,1,0, // partition 37
        0,1,1,0,1,0,0,1,0,1,1,0,1,0,0,1, // partition 38
        0,1,0,1,1,0,1,0,1,0,1,0,0,1,0,1, // partition 39
        0,1,1,1,0,0,1,1,1,1,0,0,1,1,1,0, // partition 40
        0,0,0,1,0,0,1,1,1,1,0,0,1,0,0,0, // partition 41
        0,0,1,1,0,0,1,0,0,1,0,0,1,1,0,0, // partition 42
        0,0,1,1,1,0,1,1,1,1,0,1,1,1,0,0, // partition 43
        0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0, // partition 44
        0,0,1,1,1,1,0,0,1,1,0,0,0,0,1,1, // partition 45
        0,1,1,0,0,1,1,0,1,0,0,1,1,0,0,1, // partition 46
        0,0,0,0,0,1,1,0,0,1,1,0,0,0,0,0, // partition 47
        0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,0, // partition 48
        0,0,1,0,0,1,1,1,0,0,1,0,0,0,0,0, // partition 49
        0,0,0,0,0,0,1,0,0,1,1,1,0,0,1,0, // partition 50
        0,0,0,0,0,1,0,0,1,1,1,0,0,1,0,0, // partition 51
        0,1,1,0,1,1,0,0,1,0,0,1,0,0,1,1, // partition 52
        0,0,1,1,0,1,1,0,1,1,0,0,1,0,0,1, // partition 53
        0,1,1,0,0,0,1,1,1,0,0,1,1,1,0,0, // partition 54
        0,0,1,1,1,0,0,1,1,1,0,0,0,1,1,0, // partition 55
        0,1,1,0,1,1,0,0,1,1,0,0,1,0,0,1, // partition 56
        0,1,1,0,0,0,1,1,0,0,1,1,1,0,0,1, // partition 57
        0,1,1,1,1,1,1,0,1,0,0,0,0,0,0,1, // partition 58
        0,0,0,1,1,0,0,0,1,1,1,0,0,1,1,1, // partition 59
        0,0,0,0,1,1,1,1,0,0,1,1,0,0,1,1, // partition 60
        0,0,1,1,0,0,1,1,1,1,1,1,0,0,0,0, // partition 61
        0,0,1,0,0,0,1,0,1,1,1,0,1,1,1,0, // partition 62
        0,1,0,0,0,1,0,0,0,1,1,1,0,1,1,1, // partition 63
    };

    // 3-subset partition table: 64 partitions x 16 pixels
    static readonly byte[] BC7PartitionTable3 =
    {
        0,0,1,1,0,0,1,1,0,2,2,1,2,2,2,2, // partition  0
        0,0,0,1,0,0,1,1,2,2,1,1,2,2,2,1, // partition  1
        0,0,0,0,2,0,0,1,2,2,1,1,2,2,1,1, // partition  2
        0,2,2,2,0,0,2,2,0,0,1,1,0,1,1,1, // partition  3
        0,0,0,0,0,0,0,0,1,1,2,2,1,1,2,2, // partition  4
        0,0,1,1,0,0,1,1,0,0,2,2,0,0,2,2, // partition  5
        0,0,2,2,0,0,2,2,1,1,1,1,1,1,1,1, // partition  6
        0,0,1,1,0,0,1,1,2,2,1,1,2,2,1,1, // partition  7
        0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2, // partition  8
        0,0,0,0,1,1,1,1,1,1,1,1,2,2,2,2, // partition  9
        0,0,0,0,1,1,1,1,2,2,2,2,2,2,2,2, // partition 10
        0,0,1,2,0,0,1,2,0,0,1,2,0,0,1,2, // partition 11
        0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2, // partition 12
        0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2, // partition 13
        0,0,1,1,0,1,1,2,1,1,2,2,1,2,2,2, // partition 14
        0,0,1,1,2,0,0,1,2,2,0,0,2,2,2,0, // partition 15
        0,0,0,1,0,0,1,1,0,1,1,2,1,1,2,2, // partition 16
        0,1,1,1,0,0,1,1,2,0,0,1,2,2,0,0, // partition 17
        0,0,0,0,1,1,2,2,1,1,2,2,1,1,2,2, // partition 18
        0,0,2,2,0,0,2,2,0,0,2,2,1,1,1,1, // partition 19
        0,1,1,1,0,1,1,1,0,2,2,2,0,2,2,2, // partition 20
        0,0,0,1,0,0,0,1,2,2,2,1,2,2,2,1, // partition 21
        0,0,0,0,0,0,1,1,0,1,2,2,0,1,2,2, // partition 22
        0,0,0,0,1,1,0,0,2,2,1,0,2,2,1,0, // partition 23
        0,1,2,2,0,1,2,2,0,0,1,1,0,0,0,0, // partition 24
        0,0,1,2,0,0,1,2,1,1,2,2,2,2,2,2, // partition 25
        0,1,1,0,1,2,2,1,1,2,2,1,0,1,1,0, // partition 26
        0,0,0,0,0,1,1,0,1,2,2,1,1,2,2,1, // partition 27
        0,0,2,2,1,1,0,2,1,1,0,2,0,0,2,2, // partition 28
        0,1,1,0,0,1,1,0,2,0,0,2,2,2,2,2, // partition 29
        0,0,1,1,0,1,2,2,0,1,2,2,0,0,1,1, // partition 30
        0,0,0,0,2,0,0,0,2,2,1,1,2,2,2,1, // partition 31
        0,0,0,0,0,0,0,2,1,1,2,2,1,2,2,2, // partition 32
        0,2,2,2,0,0,2,2,0,0,1,2,0,0,1,1, // partition 33
        0,0,1,1,0,0,1,2,0,0,2,2,0,2,2,2, // partition 34
        0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,0, // partition 35
        0,0,0,0,1,1,1,1,2,2,2,2,0,0,0,0, // partition 36
        0,1,2,0,1,2,0,1,2,0,1,2,0,1,2,0, // partition 37
        0,1,2,0,2,0,1,2,1,2,0,1,0,1,2,0, // partition 38
        0,0,1,1,2,2,0,0,1,1,2,2,0,0,1,1, // partition 39
        0,0,1,1,1,1,2,2,2,2,0,0,0,0,1,1, // partition 40
        0,1,0,1,0,1,0,1,2,2,2,2,2,2,2,2, // partition 41
        0,0,0,0,0,0,0,0,2,1,2,1,2,1,2,1, // partition 42
        0,0,2,2,1,1,2,2,0,0,2,2,1,1,2,2, // partition 43
        0,0,2,2,0,0,1,1,0,0,2,2,0,0,1,1, // partition 44
        0,2,2,0,1,2,2,1,0,2,2,0,1,2,2,1, // partition 45
        0,1,0,1,2,2,2,2,2,2,2,2,0,1,0,1, // partition 46
        0,0,0,0,2,1,2,1,2,1,2,1,2,1,2,1, // partition 47
        0,1,0,1,0,1,0,1,0,1,0,1,2,2,2,2, // partition 48
        0,2,2,2,0,1,1,1,0,2,2,2,0,1,1,1, // partition 49
        0,0,0,2,1,1,1,2,0,0,0,2,1,1,1,2, // partition 50
        0,0,0,0,2,1,1,2,2,1,1,2,2,1,1,2, // partition 51
        0,2,2,2,0,1,1,1,0,1,1,1,0,2,2,2, // partition 52
        0,0,0,2,1,1,1,2,1,1,1,2,0,0,0,2, // partition 53
        0,1,1,0,0,1,1,0,0,1,1,0,2,2,2,2, // partition 54
        0,0,0,0,0,0,0,0,2,1,1,2,2,1,1,2, // partition 55
        0,1,1,0,0,1,1,0,2,2,2,2,2,2,2,2, // partition 56
        0,0,2,2,0,0,1,1,0,0,1,1,0,0,2,2, // partition 57
        0,0,2,2,1,1,2,2,1,1,2,2,0,0,2,2, // partition 58
        0,0,0,0,0,0,0,0,0,0,0,0,2,1,1,2, // partition 59
        0,0,0,2,0,0,0,1,0,0,0,2,0,0,0,1, // partition 60
        0,2,2,2,1,2,2,2,0,2,2,2,1,2,2,2, // partition 61
        0,1,0,1,2,2,2,2,2,2,2,2,2,2,2,2, // partition 62
        0,1,1,1,2,0,1,1,2,2,0,1,2,2,2,0, // partition 63
    };

    // Anchor indices for 2-subset partitions (second subset anchor)
    static readonly byte[] BC7AnchorIndex2_1 =
    {
        15,15,15,15,15,15,15,15,
        15,15,15,15,15,15,15,15,
        15, 2, 8, 2, 2, 8, 8,15,
         2, 8, 2, 2, 8, 8, 2, 2,
        15,15, 6, 8, 2, 8,15,15,
         2, 8, 2, 2, 2,15,15, 6,
         6, 2, 6, 8,15,15, 2, 2,
        15,15,15,15,15, 2, 2,15,
    };

    // Anchor indices for 3-subset partitions (second subset)
    static readonly byte[] BC7AnchorIndex3_1 =
    {
         3, 3,15,15, 8, 3,15,15,
         8, 8, 6, 6, 6, 5, 3, 3,
         3, 3, 8,15, 3, 3, 6,10,
         5, 8, 8, 6, 8, 5,15,15,
         8,15, 3, 5, 6,10, 8,15,
        15, 3,15, 5,15,15,15,15,
         3,15, 5, 5, 5, 8, 5,10,
         5,10, 8,13,15,12, 3, 3,
    };

    // Anchor indices for 3-subset partitions (third subset)
    static readonly byte[] BC7AnchorIndex3_2 =
    {
        15, 8, 8, 3,15,15, 3, 8,
        15,15,15,15,15,15,15, 8,
        15, 8,15, 3,15, 8,15, 8,
         3,15, 6,10,15,15,10, 8,
        15, 3,15,10,10, 8, 9,10,
         6,15, 8,15, 3, 6, 6, 8,
        15, 3,15,15,15,15,15,15,
        15,15,15,15, 3,15,15, 8,
    };

    struct BC7BitReader
    {
        NativeArray<byte> data;
        int offset;
        int bitPos;

        public BC7BitReader(NativeArray<byte> data, int offset)
        {
            this.data = data;
            this.offset = offset;
            bitPos = 0;
        }

        public int ReadBits(int count)
        {
            int result = 0;
            for (int i = 0; i < count; i++)
            {
                int byteIdx = offset + (bitPos >> 3);
                int bitIdx = bitPos & 7;
                result |= ((data[byteIdx] >> bitIdx) & 1) << i;
                bitPos++;
            }
            return result;
        }

        public void SkipBits(int count)
        {
            bitPos += count;
        }
    }

    static void DecodeBC7Pixel(
        NativeArray<byte> data,
        int blockOffset,
        int localX,
        int localY,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        var reader = new BC7BitReader(data, blockOffset);

        // Determine mode (0-7) from leading bits
        int mode = 0;
        while (mode < 8 && reader.ReadBits(1) == 0)
            mode++;

        if (mode >= 8)
        {
            r = g = b = a = 0f;
            return;
        }

        int pixelIndex = localY * 4 + localX;

        switch (mode)
        {
            case 0:
                DecodeBC7Mode0(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
            case 1:
                DecodeBC7Mode1(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
            case 2:
                DecodeBC7Mode2(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
            case 3:
                DecodeBC7Mode3(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
            case 4:
                DecodeBC7Mode4(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
            case 5:
                DecodeBC7Mode5(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
            case 6:
                DecodeBC7Mode6(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
            default:
                DecodeBC7Mode7(ref reader, pixelIndex, out r, out g, out b, out a);
                break;
        }
    }

    static readonly byte[] BC7Weights2 = { 0, 21, 43, 64 };
    static readonly byte[] BC7Weights3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
    static readonly byte[] BC7Weights4 =
    {
        0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64,
    };
    // csharpier-ignore-end

    static int BC7Interpolate(int e0, int e1, int weight)
    {
        return (e0 * (64 - weight) + e1 * weight + 32) >> 6;
    }

    static int BC7Unquantize(int val, int bits)
    {
        if (bits >= 8)
            return val;
        val = val << (8 - bits);
        return val | (val >> bits);
    }

    // Mode 0: 3 subsets, 4-bit endpoints (RGB), 1-bit pbit, 3-bit indices
    static void DecodeBC7Mode0(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int partition = reader.ReadBits(4);

        int4x2 endpointsR,
            endpointsG,
            endpointsB;
        // 3 subsets x 2 endpoints x 4 bits = 24 bits per channel
        endpointsR.c0 = new int4(reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4), 0);
        endpointsR.c1 = new int4(reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4), 0);
        endpointsG.c0 = new int4(reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4), 0);
        endpointsG.c1 = new int4(reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4), 0);
        endpointsB.c0 = new int4(reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4), 0);
        endpointsB.c1 = new int4(reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4), 0);

        // 6 p-bits (one per endpoint)
        int pbit0 = reader.ReadBits(1);
        int pbit1 = reader.ReadBits(1);
        int pbit2 = reader.ReadBits(1);
        int pbit3 = reader.ReadBits(1);
        int pbit4 = reader.ReadBits(1);
        int pbit5 = reader.ReadBits(1);

        endpointsR.c0.x = (endpointsR.c0.x << 1) | pbit0;
        endpointsR.c1.x = (endpointsR.c1.x << 1) | pbit1;
        endpointsR.c0.y = (endpointsR.c0.y << 1) | pbit2;
        endpointsR.c1.y = (endpointsR.c1.y << 1) | pbit3;
        endpointsR.c0.z = (endpointsR.c0.z << 1) | pbit4;
        endpointsR.c1.z = (endpointsR.c1.z << 1) | pbit5;
        endpointsG.c0.x = (endpointsG.c0.x << 1) | pbit0;
        endpointsG.c1.x = (endpointsG.c1.x << 1) | pbit1;
        endpointsG.c0.y = (endpointsG.c0.y << 1) | pbit2;
        endpointsG.c1.y = (endpointsG.c1.y << 1) | pbit3;
        endpointsG.c0.z = (endpointsG.c0.z << 1) | pbit4;
        endpointsG.c1.z = (endpointsG.c1.z << 1) | pbit5;
        endpointsB.c0.x = (endpointsB.c0.x << 1) | pbit0;
        endpointsB.c1.x = (endpointsB.c1.x << 1) | pbit1;
        endpointsB.c0.y = (endpointsB.c0.y << 1) | pbit2;
        endpointsB.c1.y = (endpointsB.c1.y << 1) | pbit3;
        endpointsB.c0.z = (endpointsB.c0.z << 1) | pbit4;
        endpointsB.c1.z = (endpointsB.c1.z << 1) | pbit5;

        // Read 3-bit indices (45 bits total, anchors lose 1 bit)
        int subset = BC7PartitionTable3[partition * 16 + pixelIndex];
        int anchor0 = 0;
        int anchor1 = BC7AnchorIndex3_1[partition];
        int anchor2 = BC7AnchorIndex3_2[partition];

        int idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int bits = 3;
            if (i == anchor0 || i == anchor1 || i == anchor2)
                bits = 2;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx = val;
        }

        int er0 = BC7Unquantize(endpointsR.c0[subset], 5);
        int er1 = BC7Unquantize(endpointsR.c1[subset], 5);
        int eg0 = BC7Unquantize(endpointsG.c0[subset], 5);
        int eg1 = BC7Unquantize(endpointsG.c1[subset], 5);
        int eb0 = BC7Unquantize(endpointsB.c0[subset], 5);
        int eb1 = BC7Unquantize(endpointsB.c1[subset], 5);

        int w = BC7Weights3[idx];
        r = BC7Interpolate(er0, er1, w) * Byte2Float;
        g = BC7Interpolate(eg0, eg1, w) * Byte2Float;
        b = BC7Interpolate(eb0, eb1, w) * Byte2Float;
        a = 1f;
    }

    // Mode 1: 2 subsets, 6-bit endpoints (RGB), 1 shared pbit per subset, 3-bit indices
    static void DecodeBC7Mode1(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int partition = reader.ReadBits(6);

        int2 r0,
            r1,
            g0,
            g1,
            b0,
            b1;
        r0 = new int2(reader.ReadBits(6), reader.ReadBits(6));
        r1 = new int2(reader.ReadBits(6), reader.ReadBits(6));
        g0 = new int2(reader.ReadBits(6), reader.ReadBits(6));
        g1 = new int2(reader.ReadBits(6), reader.ReadBits(6));
        b0 = new int2(reader.ReadBits(6), reader.ReadBits(6));
        b1 = new int2(reader.ReadBits(6), reader.ReadBits(6));

        int pbit0 = reader.ReadBits(1);
        int pbit1 = reader.ReadBits(1);

        r0.x = (r0.x << 1) | pbit0;
        r1.x = (r1.x << 1) | pbit0;
        r0.y = (r0.y << 1) | pbit1;
        r1.y = (r1.y << 1) | pbit1;
        g0.x = (g0.x << 1) | pbit0;
        g1.x = (g1.x << 1) | pbit0;
        g0.y = (g0.y << 1) | pbit1;
        g1.y = (g1.y << 1) | pbit1;
        b0.x = (b0.x << 1) | pbit0;
        b1.x = (b1.x << 1) | pbit0;
        b0.y = (b0.y << 1) | pbit1;
        b1.y = (b1.y << 1) | pbit1;

        int subset = BC7PartitionTable2[partition * 16 + pixelIndex];
        int anchor1 = BC7AnchorIndex2_1[partition];

        int idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int bits = 3;
            if (i == 0 || i == anchor1)
                bits = 2;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx = val;
        }

        int er0v = BC7Unquantize(r0[subset], 7);
        int er1v = BC7Unquantize(r1[subset], 7);
        int eg0v = BC7Unquantize(g0[subset], 7);
        int eg1v = BC7Unquantize(g1[subset], 7);
        int eb0v = BC7Unquantize(b0[subset], 7);
        int eb1v = BC7Unquantize(b1[subset], 7);

        int w = BC7Weights3[idx];
        r = BC7Interpolate(er0v, er1v, w) * Byte2Float;
        g = BC7Interpolate(eg0v, eg1v, w) * Byte2Float;
        b = BC7Interpolate(eb0v, eb1v, w) * Byte2Float;
        a = 1f;
    }

    // Mode 2: 3 subsets, 5-bit endpoints (RGB), no pbit, 2-bit indices
    static void DecodeBC7Mode2(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int partition = reader.ReadBits(6);

        int4x2 endpointsR,
            endpointsG,
            endpointsB;
        endpointsR.c0 = new int4(reader.ReadBits(5), reader.ReadBits(5), reader.ReadBits(5), 0);
        endpointsR.c1 = new int4(reader.ReadBits(5), reader.ReadBits(5), reader.ReadBits(5), 0);
        endpointsG.c0 = new int4(reader.ReadBits(5), reader.ReadBits(5), reader.ReadBits(5), 0);
        endpointsG.c1 = new int4(reader.ReadBits(5), reader.ReadBits(5), reader.ReadBits(5), 0);
        endpointsB.c0 = new int4(reader.ReadBits(5), reader.ReadBits(5), reader.ReadBits(5), 0);
        endpointsB.c1 = new int4(reader.ReadBits(5), reader.ReadBits(5), reader.ReadBits(5), 0);

        int subset = BC7PartitionTable3[partition * 16 + pixelIndex];
        int anchor0 = 0;
        int anchor1 = BC7AnchorIndex3_1[partition];
        int anchor2 = BC7AnchorIndex3_2[partition];

        int idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int bits = 2;
            if (i == anchor0 || i == anchor1 || i == anchor2)
                bits = 1;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx = val;
        }

        int er0 = BC7Unquantize(endpointsR.c0[subset], 5);
        int er1 = BC7Unquantize(endpointsR.c1[subset], 5);
        int eg0 = BC7Unquantize(endpointsG.c0[subset], 5);
        int eg1 = BC7Unquantize(endpointsG.c1[subset], 5);
        int eb0 = BC7Unquantize(endpointsB.c0[subset], 5);
        int eb1 = BC7Unquantize(endpointsB.c1[subset], 5);

        int w = BC7Weights2[idx];
        r = BC7Interpolate(er0, er1, w) * Byte2Float;
        g = BC7Interpolate(eg0, eg1, w) * Byte2Float;
        b = BC7Interpolate(eb0, eb1, w) * Byte2Float;
        a = 1f;
    }

    // Mode 3: 2 subsets, 7-bit endpoints (RGB), 1 pbit per endpoint, 2-bit indices
    static void DecodeBC7Mode3(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int partition = reader.ReadBits(6);

        int2 r0,
            r1,
            g0,
            g1,
            b0,
            b1;
        r0 = new int2(reader.ReadBits(7), reader.ReadBits(7));
        r1 = new int2(reader.ReadBits(7), reader.ReadBits(7));
        g0 = new int2(reader.ReadBits(7), reader.ReadBits(7));
        g1 = new int2(reader.ReadBits(7), reader.ReadBits(7));
        b0 = new int2(reader.ReadBits(7), reader.ReadBits(7));
        b1 = new int2(reader.ReadBits(7), reader.ReadBits(7));

        int pbit0 = reader.ReadBits(1);
        int pbit1 = reader.ReadBits(1);
        int pbit2 = reader.ReadBits(1);
        int pbit3 = reader.ReadBits(1);

        r0.x = (r0.x << 1) | pbit0;
        r1.x = (r1.x << 1) | pbit1;
        r0.y = (r0.y << 1) | pbit2;
        r1.y = (r1.y << 1) | pbit3;
        g0.x = (g0.x << 1) | pbit0;
        g1.x = (g1.x << 1) | pbit1;
        g0.y = (g0.y << 1) | pbit2;
        g1.y = (g1.y << 1) | pbit3;
        b0.x = (b0.x << 1) | pbit0;
        b1.x = (b1.x << 1) | pbit1;
        b0.y = (b0.y << 1) | pbit2;
        b1.y = (b1.y << 1) | pbit3;

        int subset = BC7PartitionTable2[partition * 16 + pixelIndex];
        int anchor1 = BC7AnchorIndex2_1[partition];

        int idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int bits = 2;
            if (i == 0 || i == anchor1)
                bits = 1;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx = val;
        }

        int er0v = BC7Unquantize(r0[subset], 8);
        int er1v = BC7Unquantize(r1[subset], 8);
        int eg0v = BC7Unquantize(g0[subset], 8);
        int eg1v = BC7Unquantize(g1[subset], 8);
        int eb0v = BC7Unquantize(b0[subset], 8);
        int eb1v = BC7Unquantize(b1[subset], 8);

        int w = BC7Weights2[idx];
        r = BC7Interpolate(er0v, er1v, w) * Byte2Float;
        g = BC7Interpolate(eg0v, eg1v, w) * Byte2Float;
        b = BC7Interpolate(eb0v, eb1v, w) * Byte2Float;
        a = 1f;
    }

    // Mode 4: 1 subset, 5-bit RGB + 6-bit A, rotation, 2-bit color + 3-bit alpha indices (or swapped)
    static void DecodeBC7Mode4(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int rotation = reader.ReadBits(2);
        int idxMode = reader.ReadBits(1);

        int r0 = reader.ReadBits(5);
        int r1 = reader.ReadBits(5);
        int g0 = reader.ReadBits(5);
        int g1 = reader.ReadBits(5);
        int b0 = reader.ReadBits(5);
        int b1 = reader.ReadBits(5);
        int a0 = reader.ReadBits(6);
        int a1 = reader.ReadBits(6);

        // Read 2-bit and 3-bit index sets
        int idx2 = 0,
            idx3 = 0;

        // 2-bit indices (31 bits: anchor pixel has 1 bit)
        for (int i = 0; i < 16; i++)
        {
            int bits = (i == 0) ? 1 : 2;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx2 = val;
        }

        // 3-bit indices (47 bits: anchor pixel has 2 bits)
        for (int i = 0; i < 16; i++)
        {
            int bits = (i == 0) ? 2 : 3;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx3 = val;
        }

        int colorIdx,
            alphaIdx;
        if (idxMode == 0)
        {
            colorIdx = idx2;
            alphaIdx = idx3;
        }
        else
        {
            colorIdx = idx3;
            alphaIdx = idx2;
        }

        byte[] colorWeights = idxMode == 0 ? BC7Weights2 : BC7Weights3;
        byte[] alphaWeights = idxMode == 0 ? BC7Weights3 : BC7Weights2;

        int ri = BC7Interpolate(BC7Unquantize(r0, 5), BC7Unquantize(r1, 5), colorWeights[colorIdx]);
        int gi = BC7Interpolate(BC7Unquantize(g0, 5), BC7Unquantize(g1, 5), colorWeights[colorIdx]);
        int bi = BC7Interpolate(BC7Unquantize(b0, 5), BC7Unquantize(b1, 5), colorWeights[colorIdx]);
        int ai = BC7Interpolate(BC7Unquantize(a0, 6), BC7Unquantize(a1, 6), alphaWeights[alphaIdx]);

        ApplyBC7Rotation(rotation, ref ri, ref gi, ref bi, ref ai);
        r = ri * Byte2Float;
        g = gi * Byte2Float;
        b = bi * Byte2Float;
        a = ai * Byte2Float;
    }

    // Mode 5: 1 subset, 7-bit RGB + 8-bit A, rotation, separate 2-bit color and alpha indices
    static void DecodeBC7Mode5(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int rotation = reader.ReadBits(2);

        int r0 = reader.ReadBits(7);
        int r1 = reader.ReadBits(7);
        int g0 = reader.ReadBits(7);
        int g1 = reader.ReadBits(7);
        int b0 = reader.ReadBits(7);
        int b1 = reader.ReadBits(7);
        int a0 = reader.ReadBits(8);
        int a1 = reader.ReadBits(8);

        int colorIdx = 0,
            alphaIdx = 0;

        for (int i = 0; i < 16; i++)
        {
            int bits = (i == 0) ? 1 : 2;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                colorIdx = val;
        }

        for (int i = 0; i < 16; i++)
        {
            int bits = (i == 0) ? 1 : 2;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                alphaIdx = val;
        }

        int ri = BC7Interpolate(BC7Unquantize(r0, 7), BC7Unquantize(r1, 7), BC7Weights2[colorIdx]);
        int gi = BC7Interpolate(BC7Unquantize(g0, 7), BC7Unquantize(g1, 7), BC7Weights2[colorIdx]);
        int bi = BC7Interpolate(BC7Unquantize(b0, 7), BC7Unquantize(b1, 7), BC7Weights2[colorIdx]);
        int ai = BC7Interpolate(BC7Unquantize(a0, 8), BC7Unquantize(a1, 8), BC7Weights2[alphaIdx]);

        ApplyBC7Rotation(rotation, ref ri, ref gi, ref bi, ref ai);
        r = ri * Byte2Float;
        g = gi * Byte2Float;
        b = bi * Byte2Float;
        a = ai * Byte2Float;
    }

    // Mode 6: 1 subset, 7-bit RGBA + 1 pbit per endpoint, 4-bit indices
    static void DecodeBC7Mode6(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int r0 = reader.ReadBits(7);
        int r1 = reader.ReadBits(7);
        int g0 = reader.ReadBits(7);
        int g1 = reader.ReadBits(7);
        int b0 = reader.ReadBits(7);
        int b1 = reader.ReadBits(7);
        int a0 = reader.ReadBits(7);
        int a1 = reader.ReadBits(7);

        int pbit0 = reader.ReadBits(1);
        int pbit1 = reader.ReadBits(1);

        r0 = (r0 << 1) | pbit0;
        r1 = (r1 << 1) | pbit1;
        g0 = (g0 << 1) | pbit0;
        g1 = (g1 << 1) | pbit1;
        b0 = (b0 << 1) | pbit0;
        b1 = (b1 << 1) | pbit1;
        a0 = (a0 << 1) | pbit0;
        a1 = (a1 << 1) | pbit1;

        int idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int bits = (i == 0) ? 3 : 4;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx = val;
        }

        int w = BC7Weights4[idx];
        int ri = BC7Interpolate(BC7Unquantize(r0, 8), BC7Unquantize(r1, 8), w);
        int gi = BC7Interpolate(BC7Unquantize(g0, 8), BC7Unquantize(g1, 8), w);
        int bi = BC7Interpolate(BC7Unquantize(b0, 8), BC7Unquantize(b1, 8), w);
        int ai = BC7Interpolate(BC7Unquantize(a0, 8), BC7Unquantize(a1, 8), w);

        r = ri * Byte2Float;
        g = gi * Byte2Float;
        b = bi * Byte2Float;
        a = ai * Byte2Float;
    }

    // Mode 7: 2 subsets, 5-bit RGBA + 1 pbit per endpoint, 2-bit indices
    static void DecodeBC7Mode7(
        ref BC7BitReader reader,
        int pixelIndex,
        out float r,
        out float g,
        out float b,
        out float a
    )
    {
        int partition = reader.ReadBits(6);

        int2 r0,
            r1,
            g0,
            g1,
            b0,
            b1,
            a0,
            a1;
        r0 = new int2(reader.ReadBits(5), reader.ReadBits(5));
        r1 = new int2(reader.ReadBits(5), reader.ReadBits(5));
        g0 = new int2(reader.ReadBits(5), reader.ReadBits(5));
        g1 = new int2(reader.ReadBits(5), reader.ReadBits(5));
        b0 = new int2(reader.ReadBits(5), reader.ReadBits(5));
        b1 = new int2(reader.ReadBits(5), reader.ReadBits(5));
        a0 = new int2(reader.ReadBits(5), reader.ReadBits(5));
        a1 = new int2(reader.ReadBits(5), reader.ReadBits(5));

        int pbit0 = reader.ReadBits(1);
        int pbit1 = reader.ReadBits(1);
        int pbit2 = reader.ReadBits(1);
        int pbit3 = reader.ReadBits(1);

        r0.x = (r0.x << 1) | pbit0;
        r1.x = (r1.x << 1) | pbit1;
        r0.y = (r0.y << 1) | pbit2;
        r1.y = (r1.y << 1) | pbit3;
        g0.x = (g0.x << 1) | pbit0;
        g1.x = (g1.x << 1) | pbit1;
        g0.y = (g0.y << 1) | pbit2;
        g1.y = (g1.y << 1) | pbit3;
        b0.x = (b0.x << 1) | pbit0;
        b1.x = (b1.x << 1) | pbit1;
        b0.y = (b0.y << 1) | pbit2;
        b1.y = (b1.y << 1) | pbit3;
        a0.x = (a0.x << 1) | pbit0;
        a1.x = (a1.x << 1) | pbit1;
        a0.y = (a0.y << 1) | pbit2;
        a1.y = (a1.y << 1) | pbit3;

        int subset = BC7PartitionTable2[partition * 16 + pixelIndex];
        int anchor1 = BC7AnchorIndex2_1[partition];

        int idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int bits = 2;
            if (i == 0 || i == anchor1)
                bits = 1;
            int val = reader.ReadBits(bits);
            if (i == pixelIndex)
                idx = val;
        }

        int w = BC7Weights2[idx];
        int ri = BC7Interpolate(BC7Unquantize(r0[subset], 6), BC7Unquantize(r1[subset], 6), w);
        int gi = BC7Interpolate(BC7Unquantize(g0[subset], 6), BC7Unquantize(g1[subset], 6), w);
        int bi = BC7Interpolate(BC7Unquantize(b0[subset], 6), BC7Unquantize(b1[subset], 6), w);
        int ai = BC7Interpolate(BC7Unquantize(a0[subset], 6), BC7Unquantize(a1[subset], 6), w);

        r = ri * Byte2Float;
        g = gi * Byte2Float;
        b = bi * Byte2Float;
        a = ai * Byte2Float;
    }

    static void ApplyBC7Rotation(int rotation, ref int r, ref int g, ref int b, ref int a)
    {
        switch (rotation)
        {
            case 1:
                (a, r) = (r, a);
                break;
            case 2:
                (a, g) = (g, a);
                break;
            case 3:
                (a, b) = (b, a);
                break;
        }
    }
}
