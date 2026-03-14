using System;
using System.Runtime.CompilerServices;
using BurstPQS.CompilerServices;
using KSPTextureLoader;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Map;

/// <summary>
/// This class is closer to a collection of individual <see cref="IMapSO"/>
/// implementations that can be used to convert a <see cref="Texture2D"/>
/// into a <see cref="BurstMapSO"/>.
/// </summary>
[BurstCompile(FloatMode = FloatMode.Fast)]
public static partial class TextureMapSO
{
    readonly struct FormatMapSO<T>(T texture) : IMapSO
        where T : ICPUTexture2D
    {
        readonly T texture = texture;

        public readonly int Width => texture.Width;
        public readonly int Height => texture.Height;
        public readonly MapSO.MapDepth Depth => MapSO.MapDepth.RGBA;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetPixelFloat(int x, int y) => texture.GetPixel(x, y).grayscale;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color GetPixelColor(int x, int y) => texture.GetPixel(x, y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color32 GetPixelColor32(int x, int y) => texture.GetPixel32(x, y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HeightAlpha GetPixelHeightAlpha(int x, int y)
        {
            Color c = texture.GetPixel(x, y);
            return new(c.r, c.a);
        }

        struct BilinearCoords
        {
            public int minX,
                maxX,
                minY,
                maxY;
            public float midX,
                midY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly BilinearCoords ConstructBilinearCoords(float x, float y)
        {
            var coords = new float2(x, y);
            var dims = new int2(texture.Width, texture.Height);

            coords = math.abs(coords - math.floor(coords));
            var min = (int2)math.floor(coords);
            var center = coords * dims;
            var max = (int2)math.ceil(center) % dims;
            var mid = center - min;

            return new()
            {
                minX = min.x,
                minY = min.y,
                maxX = max.x,
                maxY = max.y,
                midX = mid.x,
                midY = mid.y,
            };
        }

        public float GetPixelFloat(float x, float y)
        {
            var c = ConstructBilinearCoords(x, y);
            return Mathf.Lerp(
                Mathf.Lerp(GetPixelFloat(c.minX, c.minY), GetPixelFloat(c.maxX, c.minY), c.midX),
                Mathf.Lerp(GetPixelFloat(c.minX, c.maxY), GetPixelFloat(c.maxX, c.maxY), c.midX),
                c.midY
            );
        }

        public float GetPixelFloat(double x, double y) => GetPixelFloat((float)x, (float)y);

        public Color GetPixelColor(float x, float y)
        {
            var c = ConstructBilinearCoords(x, y);
            return Color.Lerp(
                Color.Lerp(GetPixelColor(c.minX, c.minY), GetPixelColor(c.maxX, c.minY), c.midX),
                Color.Lerp(GetPixelColor(c.minX, c.maxY), GetPixelColor(c.maxX, c.maxY), c.midX),
                c.midY
            );
        }

        public Color GetPixelColor(double x, double y) => GetPixelColor((float)x, (float)y);

        public Color32 GetPixelColor32(float x, float y)
        {
            var c = ConstructBilinearCoords(x, y);
            return Color32.Lerp(
                Color32.Lerp(
                    GetPixelColor32(c.minX, c.minY),
                    GetPixelColor32(c.maxX, c.minY),
                    c.midX
                ),
                Color32.Lerp(
                    GetPixelColor32(c.minX, c.maxY),
                    GetPixelColor32(c.maxX, c.maxY),
                    c.midX
                ),
                c.midY
            );
        }

        public Color32 GetPixelColor32(double x, double y) => GetPixelColor32((float)x, (float)y);

        public HeightAlpha GetPixelHeightAlpha(float x, float y)
        {
            var c = ConstructBilinearCoords(x, y);
            return HeightAlpha.Lerp(
                HeightAlpha.Lerp(
                    GetPixelHeightAlpha(c.minX, c.minY),
                    GetPixelHeightAlpha(c.maxX, c.minY),
                    c.midX
                ),
                HeightAlpha.Lerp(
                    GetPixelHeightAlpha(c.minX, c.maxY),
                    GetPixelHeightAlpha(c.maxX, c.maxY),
                    c.midX
                ),
                c.midY
            );
        }

        public HeightAlpha GetPixelHeightAlpha(double x, double y) =>
            GetPixelHeightAlpha((float)x, (float)y);
    }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.Alpha8>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct Alpha8 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.ARGB32>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct ARGB32 : IMapSO { }

    /// <summary>
    /// ARGB4444 format. Each pixel is a 16-bit value with 4 bits per channel
    /// in the order A, R, G, B (high to low bits).
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.ARGB4444>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct ARGB4444 : IMapSO { }

    /// <summary>
    /// BC4 compressed format. Encodes a single channel (R) in 8-byte blocks covering
    /// 4x4 pixels (4 bits/pixel). Each block stores two 8-bit endpoints and a 4x4 grid
    /// of 3-bit indices selecting from a 6- or 8-value palette interpolated between them.
    /// Uses the same alpha block encoding as DXT5.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC4>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC4 : IMapSO { }

    /// <summary>
    /// BC5 compressed format. Encodes two channels (R, G) in 16-byte blocks covering
    /// 4x4 pixels (8 bits/pixel). Each block consists of two independent BC4 blocks
    /// side by side, one for the red channel and one for the green channel.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC5>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC5 : IMapSO { }

    /// <summary>
    /// BC6H compressed format. Encodes HDR RGB (no alpha) in 16-byte blocks covering
    /// 4x4 pixels (8 bits/pixel). Supports 14 modes (0-13) with 1 or 2 subsets,
    /// half-float endpoints (10-16 bits) with optional delta encoding, and 3- or 4-bit
    /// indices. Endpoints are unquantized to 16-bit half-precision floats and interpolated.
    /// Comes in signed (<c>BC6H_SF16</c>) and unsigned (<c>BC6H_UF16</c>) variants.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC6H>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC6H : IMapSO { }

    /// <summary>
    /// BC7 compressed format. Encodes RGBA in 16-byte blocks covering 4x4 pixels
    /// (8 bits/pixel). Supports 8 modes (0-7) with varying numbers of subsets (1-3),
    /// endpoint precision (4-8 bits), index precision (2-4 bits), and optional
    /// per-endpoint p-bits, channel rotation, and separate color/alpha index sets.
    /// Partition tables select which pixels belong to which subset.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BC7>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BC7 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.BGRA32>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct BGRA32 : IMapSO { }

    /// <summary>
    /// DXT1/BC1 compressed format. Encodes RGB with optional 1-bit alpha in 8-byte blocks
    /// covering 4x4 pixels (4 bits/pixel). Each block stores two RGB565 color endpoints and
    /// a 4x4 grid of 2-bit indices selecting from a 4-color palette interpolated between them.
    /// When <c>color0 &lt;= color1</c>, index 3 produces transparent black (1-bit alpha).
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.DXT1>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct DXT1 : IMapSO { }

    /// <summary>
    /// DXT5/BC3 compressed format. Encodes RGBA in 16-byte blocks covering 4x4 pixels
    /// (8 bits/pixel). Each block consists of an 8-byte alpha block followed by an 8-byte
    /// DXT1 color block. The alpha block stores two 8-bit endpoints and a 4x4 grid of
    /// 3-bit indices selecting from an 8-value palette interpolated between them.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.DXT5>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct DXT5 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.R8>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct R8 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.R16>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct R16 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RA16>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RA16 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RFloat>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RFloat : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RG16>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RG16 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGB24>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGB24 : IMapSO { }

    /// <summary>
    /// RGB565 format. Each pixel is a 16-bit value with 5 bits red, 6 bits green,
    /// 5 bits blue (high to low bits). No alpha channel.
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGB565>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGB565 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGBA32>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGBA32 : IMapSO { }

    /// <summary>
    /// RGBA4444 format. Each pixel is a 16-bit value with 4 bits per channel
    /// in the order R, G, B, A (high to low bits).
    /// </summary>
    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGBA4444>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGBA4444 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGBAFloat>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGBAFloat : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGBAHalf>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGBAHalf : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGFloat>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGFloat : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RGHalf>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RGHalf : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.RHalf>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct RHalf : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.KopernicusPalette4>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct KopernicusPalette4 : IMapSO { }

    [StructInherit(typeof(FormatMapSO<CPUTexture2D.KopernicusPalette8>), Name = "mapSO")]
    [BurstCompile]
    internal partial struct KopernicusPalette8 : IMapSO { }

    public static BurstMapSO Create(CPUTexture2D texture)
    {
        var width = texture.Width;
        var height = texture.Height;
        var mipCount = texture.MipCount;
        var data = texture.GetRawTextureData();

        return texture switch
        {
            CPUTexture2D<CPUTexture2D.Alpha8> => BurstMapSO.Create(
                new Alpha8(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.ARGB32> => BurstMapSO.Create(
                new ARGB32(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.ARGB4444> => BurstMapSO.Create(
                new ARGB4444(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.BC4> => BurstMapSO.Create(
                new BC4(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.BC5> => BurstMapSO.Create(
                new BC5(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.BC6H> => BurstMapSO.Create(
                new BC6H(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.BC7> => BurstMapSO.Create(
                new BC7(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.BGRA32> => BurstMapSO.Create(
                new BGRA32(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.DXT1> => BurstMapSO.Create(
                new DXT1(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.DXT5> => BurstMapSO.Create(
                new DXT5(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.R8> => BurstMapSO.Create(
                new R8(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.R16> => BurstMapSO.Create(
                new R16(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RA16> => BurstMapSO.Create(
                new RA16(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RFloat> => BurstMapSO.Create(
                new RFloat(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RG16> => BurstMapSO.Create(
                new RG16(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGB24> => BurstMapSO.Create(
                new RGB24(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGB565> => BurstMapSO.Create(
                new RGB565(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGBA32> => BurstMapSO.Create(
                new RGBA32(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGBA4444> => BurstMapSO.Create(
                new RGBA4444(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGBAFloat> => BurstMapSO.Create(
                new RGBAFloat(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGBAHalf> => BurstMapSO.Create(
                new RGBAHalf(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGFloat> => BurstMapSO.Create(
                new RGFloat(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RGHalf> => BurstMapSO.Create(
                new RGHalf(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.RHalf> => BurstMapSO.Create(
                new RHalf(new(data, width, height, mipCount))
            ),
            CPUTexture2D<CPUTexture2D.KopernicusPalette4> => BurstMapSO.Create(
                new KopernicusPalette4(new(data, width, height))
            ),
            CPUTexture2D<CPUTexture2D.KopernicusPalette8> => BurstMapSO.Create(
                new KopernicusPalette8(new(data, width, height))
            ),
            _ => throw new NotSupportedException(
                $"CPU texture of type {texture.GetType().FullName} is not supported by TextureMapSO"
            ),
        };
    }

    public static BurstMapSO Create(Texture2D texture)
    {
        var width = texture.width;
        var height = texture.height;
        var mipCount = texture.mipmapCount;
        var data = texture.GetRawTextureData<byte>();

        return texture.format switch
        {
            TextureFormat.Alpha8 => BurstMapSO.Create(
                new Alpha8(new(data, width, height, mipCount))
            ),
            TextureFormat.ARGB32 => BurstMapSO.Create(
                new ARGB32(new(data, width, height, mipCount))
            ),
            TextureFormat.ARGB4444 => BurstMapSO.Create(
                new ARGB32(new(data, width, height, mipCount))
            ),
            TextureFormat.BC4 => BurstMapSO.Create(new BC4(new(data, width, height, mipCount))),
            TextureFormat.BC5 => BurstMapSO.Create(new BC5(new(data, width, height, mipCount))),
            TextureFormat.BC6H => BurstMapSO.Create(new BC6H(new(data, width, height, mipCount))),
            TextureFormat.BC7 => BurstMapSO.Create(new BC7(new(data, width, height, mipCount))),
            TextureFormat.BGRA32 => BurstMapSO.Create(
                new BGRA32(new(data, width, height, mipCount))
            ),
            TextureFormat.DXT1 => BurstMapSO.Create(new DXT1(new(data, width, height, mipCount))),
            TextureFormat.DXT5 => BurstMapSO.Create(new DXT5(new(data, width, height, mipCount))),
            TextureFormat.R8 => BurstMapSO.Create(new R8(new(data, width, height, mipCount))),
            TextureFormat.R16 => BurstMapSO.Create(new R16(new(data, width, height, mipCount))),
            TextureFormat.RFloat => BurstMapSO.Create(
                new RFloat(new(data, width, height, mipCount))
            ),
            TextureFormat.RG16 => BurstMapSO.Create(new RG16(new(data, width, height, mipCount))),
            TextureFormat.RGB24 => BurstMapSO.Create(new RGB24(new(data, width, height, mipCount))),
            TextureFormat.RGB565 => BurstMapSO.Create(
                new RGB565(new(data, width, height, mipCount))
            ),
            TextureFormat.RGBA32 => BurstMapSO.Create(
                new RGBA32(new(data, width, height, mipCount))
            ),
            TextureFormat.RGBA4444 => BurstMapSO.Create(
                new RGBA4444(new(data, width, height, mipCount))
            ),
            TextureFormat.RGBAFloat => BurstMapSO.Create(
                new RGBAFloat(new(data, width, height, mipCount))
            ),
            TextureFormat.RGBAHalf => BurstMapSO.Create(
                new RGBAHalf(new(data, width, height, mipCount))
            ),
            TextureFormat.RGFloat => BurstMapSO.Create(
                new RGFloat(new(data, width, height, mipCount))
            ),
            TextureFormat.RGHalf => BurstMapSO.Create(
                new RGHalf(new(data, width, height, mipCount))
            ),
            TextureFormat.RHalf => BurstMapSO.Create(new RHalf(new(data, width, height, mipCount))),
            _ => throw new NotSupportedException(
                $"texture format {texture.format} is not supported by TextureMapSO"
            ),
        };
    }
}
