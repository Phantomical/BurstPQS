using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

[BurstCompile(FloatMode = FloatMode.Fast)]
public static partial class TextureMapSO
{
    const float Byte2Float = 1f / 255f;
    const float UShort2Float = 1f / 65535f;

    public static BurstMapSO Create(Texture2D texture, MapSO.MapDepth depth)
    {
        switch (texture.format)
        {
            case TextureFormat.Alpha8:
                return BurstMapSO.Create(new Alpha8(texture, depth));
            case TextureFormat.ARGB32:
                return BurstMapSO.Create(new ARGB32(texture, depth));
            case TextureFormat.ARGB4444:
                return BurstMapSO.Create(new ARGB4444(texture, depth));
            case TextureFormat.BC4:
                return BurstMapSO.Create(new BC4(texture, depth));
            case TextureFormat.BC5:
                return BurstMapSO.Create(new BC5(texture, depth));
            case TextureFormat.BC6H:
                return BurstMapSO.Create(new BC6H(texture, depth));
            case TextureFormat.BC7:
                return BurstMapSO.Create(new BC7(texture, depth));
            case TextureFormat.BGRA32:
                return BurstMapSO.Create(new BGRA32(texture, depth));
            case TextureFormat.DXT1:
                return BurstMapSO.Create(new DXT1(texture, depth));
            case TextureFormat.DXT5:
                return BurstMapSO.Create(new DXT5(texture, depth));
            case TextureFormat.R8:
                return BurstMapSO.Create(new R8(texture, depth));
            case TextureFormat.R16:
                return BurstMapSO.Create(new R16(texture, depth));
            case TextureFormat.RFloat:
                return BurstMapSO.Create(new RFloat(texture, depth));
            case TextureFormat.RG16:
                return BurstMapSO.Create(new RG16(texture, depth));
            case TextureFormat.RGB24:
                return BurstMapSO.Create(new RGB24(texture, depth));
            case TextureFormat.RGB565:
                return BurstMapSO.Create(new RGB565(texture, depth));
            case TextureFormat.RGBA32:
                return BurstMapSO.Create(new RGBA32(texture, depth));
            case TextureFormat.RGBA4444:
                return BurstMapSO.Create(new RGBA4444(texture, depth));
            case TextureFormat.RGBAFloat:
                return BurstMapSO.Create(new RGBAFloat(texture, depth));
            case TextureFormat.RGBAHalf:
                return BurstMapSO.Create(new RGBAHalf(texture, depth));
            case TextureFormat.RGFloat:
                return BurstMapSO.Create(new RGFloat(texture, depth));
            case TextureFormat.RHalf:
                return BurstMapSO.Create(new RHalf(texture, depth));
            case TextureFormat.RGHalf:
                return BurstMapSO.Create(new RGHalf(texture, depth));

            default:
                throw new NotSupportedException(
                    $"texture format {texture.format} is not supported by TextureMapSO"
                );
        }
    }

    static void ValidateFormat(Texture2D texture, TextureFormat expected)
    {
        if (texture.format != expected)
            throw new ArgumentException(
                $"Expected texture format {expected} but got {texture.format}",
                nameof(texture)
            );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int PixelIndex(int x, int y, int width, int height)
    {
        x = math.clamp(x, 0, width - 1);
        y = math.clamp(y, 0, height - 1);
        return y * width + x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float DepthToFloat(float r, float g, float b, float a, MapSO.MapDepth depth)
    {
        return depth switch
        {
            MapSO.MapDepth.Greyscale => r,
            MapSO.MapDepth.HeightAlpha => (r + g) * 0.5f,
            MapSO.MapDepth.RGB => (r + g + b) * (1f / 3f),
            MapSO.MapDepth.RGBA => (r + g + b + a) * 0.25f,
            _ => r,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Color DepthToColor(float r, float g, float b, float a, MapSO.MapDepth depth)
    {
        return depth switch
        {
            MapSO.MapDepth.Greyscale => new Color(r, r, r, 1f),
            MapSO.MapDepth.HeightAlpha => new Color(r, r, r, g),
            MapSO.MapDepth.RGB => new Color(r, g, b, 1f),
            MapSO.MapDepth.RGBA => new Color(r, g, b, a),
            _ => new Color(r, r, r, 1f),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Color32 DepthToColor32(float r, float g, float b, float a, MapSO.MapDepth depth)
    {
        return depth switch
        {
            MapSO.MapDepth.Greyscale => new Color32(F2B(r), F2B(r), F2B(r), 255),
            MapSO.MapDepth.HeightAlpha => new Color32(F2B(r), F2B(r), F2B(r), F2B(g)),
            MapSO.MapDepth.RGB => new Color32(F2B(r), F2B(g), F2B(b), 255),
            MapSO.MapDepth.RGBA => new Color32(F2B(r), F2B(g), F2B(b), F2B(a)),
            _ => new Color32(F2B(r), F2B(r), F2B(r), 255),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static HeightAlpha DepthToHeightAlpha(float r, float g, float b, float a, MapSO.MapDepth depth)
    {
        return depth switch
        {
            MapSO.MapDepth.Greyscale => new HeightAlpha(r, 1f),
            MapSO.MapDepth.HeightAlpha => new HeightAlpha(r, g),
            MapSO.MapDepth.RGB => new HeightAlpha(r, 1f),
            MapSO.MapDepth.RGBA => new HeightAlpha(r, a),
            _ => new HeightAlpha(r, 1f),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte F2B(float f) => (byte)(math.saturate(f) * 255f + 0.5f);

    // ---- Block compression helpers ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void BlockCoords(
        int x,
        int y,
        int width,
        out int blockIndex,
        out int localX,
        out int localY,
        int blockSize
    )
    {
        int blockX = x >> 2;
        int blockY = y >> 2;
        localX = x & 3;
        localY = y & 3;
        int blocksPerRow = (width + 3) >> 2;
        blockIndex = (blockY * blocksPerRow + blockX) * blockSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void UnpackRGB565(ushort c, out float r, out float g, out float b)
    {
        r = ((c >> 11) & 0x1F) * (1f / 31f);
        g = ((c >> 5) & 0x3F) * (1f / 63f);
        b = (c & 0x1F) * (1f / 31f);
    }

    static void DecodeDXT1Pixel(
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
        ushort c0 = (ushort)(data[blockOffset] | (data[blockOffset + 1] << 8));
        ushort c1 = (ushort)(data[blockOffset + 2] | (data[blockOffset + 3] << 8));

        UnpackRGB565(c0, out float r0, out float g0, out float b0);
        UnpackRGB565(c1, out float r1, out float g1, out float b1);

        int indexByte = data[blockOffset + 4 + localY];
        int code = (indexByte >> (localX * 2)) & 3;

        if (c0 > c1)
        {
            switch (code)
            {
                case 0:
                    r = r0;
                    g = g0;
                    b = b0;
                    a = 1f;
                    return;
                case 1:
                    r = r1;
                    g = g1;
                    b = b1;
                    a = 1f;
                    return;
                case 2:
                    r = (2f * r0 + r1) * (1f / 3f);
                    g = (2f * g0 + g1) * (1f / 3f);
                    b = (2f * b0 + b1) * (1f / 3f);
                    a = 1f;
                    return;
                default:
                    r = (r0 + 2f * r1) * (1f / 3f);
                    g = (g0 + 2f * g1) * (1f / 3f);
                    b = (b0 + 2f * b1) * (1f / 3f);
                    a = 1f;
                    return;
            }
        }
        else
        {
            switch (code)
            {
                case 0:
                    r = r0;
                    g = g0;
                    b = b0;
                    a = 1f;
                    return;
                case 1:
                    r = r1;
                    g = g1;
                    b = b1;
                    a = 1f;
                    return;
                case 2:
                    r = (r0 + r1) * 0.5f;
                    g = (g0 + g1) * 0.5f;
                    b = (b0 + b1) * 0.5f;
                    a = 1f;
                    return;
                default:
                    r = 0f;
                    g = 0f;
                    b = 0f;
                    a = 0f;
                    return;
            }
        }
    }

    static float DecodeBC4Block(
        Unity.Collections.NativeArray<byte> data,
        int blockOffset,
        int localX,
        int localY
    )
    {
        float a0 = data[blockOffset] * Byte2Float;
        float a1 = data[blockOffset + 1] * Byte2Float;

        int bitOffset = (localY * 4 + localX) * 3;
        int byteIndex = blockOffset + 2 + bitOffset / 8;
        int bitShift = bitOffset % 8;

        int code;
        if (bitShift <= 5)
        {
            code = (data[byteIndex] >> bitShift) & 7;
        }
        else
        {
            code = ((data[byteIndex] >> bitShift) | (data[byteIndex + 1] << (8 - bitShift))) & 7;
        }

        if (data[blockOffset] > data[blockOffset + 1])
        {
            return code switch
            {
                0 => a0,
                1 => a1,
                2 => (6f * a0 + 1f * a1) * (1f / 7f),
                3 => (5f * a0 + 2f * a1) * (1f / 7f),
                4 => (4f * a0 + 3f * a1) * (1f / 7f),
                5 => (3f * a0 + 4f * a1) * (1f / 7f),
                6 => (2f * a0 + 5f * a1) * (1f / 7f),
                _ => (1f * a0 + 6f * a1) * (1f / 7f),
            };
        }
        else
        {
            return code switch
            {
                0 => a0,
                1 => a1,
                2 => (4f * a0 + 1f * a1) * (1f / 5f),
                3 => (3f * a0 + 2f * a1) * (1f / 5f),
                4 => (2f * a0 + 3f * a1) * (1f / 5f),
                5 => (1f * a0 + 4f * a1) * (1f / 5f),
                6 => 0f,
                _ => 1f,
            };
        }
    }
}
