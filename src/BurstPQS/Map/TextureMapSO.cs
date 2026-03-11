using System;
using System.Runtime.CompilerServices;
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
    struct FormatMapSO<T>(T texture) : IMapSO
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
                new ARGB444(new(data, width, height, mipCount))
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
            TextureFormat.BC4 => BurstMapSO.Create(
                new BC4(new(data, width, height, mipCount))
            ),
            TextureFormat.BC5 => BurstMapSO.Create(
                new BC5(new(data, width, height, mipCount))
            ),
            TextureFormat.BC6H => BurstMapSO.Create(
                new BC6H(new(data, width, height, mipCount))
            ),
            TextureFormat.BC7 => BurstMapSO.Create(
                new BC7(new(data, width, height, mipCount))
            ),
            TextureFormat.BGRA32 => BurstMapSO.Create(
                new BGRA32(new(data, width, height, mipCount))
            ),
            TextureFormat.DXT1 => BurstMapSO.Create(
                new DXT1(new(data, width, height, mipCount))
            ),
            TextureFormat.DXT5 => BurstMapSO.Create(
                new DXT5(new(data, width, height, mipCount))
            ),
            TextureFormat.R8 => BurstMapSO.Create(
                new R8(new(data, width, height, mipCount))
            ),
            TextureFormat.R16 => BurstMapSO.Create(
                new R16(new(data, width, height, mipCount))
            ),
            TextureFormat.RFloat => BurstMapSO.Create(
                new RFloat(new(data, width, height, mipCount))
            ),
            TextureFormat.RG16 => BurstMapSO.Create(
                new RG16(new(data, width, height, mipCount))
            ),
            TextureFormat.RGB24 => BurstMapSO.Create(
                new RGB24(new(data, width, height, mipCount))
            ),
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
            TextureFormat.RHalf => BurstMapSO.Create(
                new RHalf(new(data, width, height, mipCount))
            ),
            _ => throw new NotSupportedException(
                $"texture format {texture.format} is not supported by TextureMapSO"
            ),
        };
    }
}
