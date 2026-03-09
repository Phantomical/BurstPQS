using System.Runtime.CompilerServices;
using BurstPQS.Map;
using Kopernicus.Components;
using Kopernicus.OnDemand;
using KSPTextureLoader;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Kopernicus.Map;

/// <summary>
/// Adapter structs that wrap KSPTextureLoader's <see cref="CPUTexture2D"/> format types
/// and implement <see cref="IMapSO"/> for use with <see cref="BurstMapSO"/>.
/// </summary>
[BurstCompile]
static partial class BurstKopernicusMapSO
{
    public static BurstMapSO Create(MapSODemand mapSO)
    {
        if (!mapSO.IsLoaded)
            mapSO.Load();

        if (!mapSO.IsLoaded)
            return BurstMapSO.Create(new InvalidMapSO());

        return Create((KopernicusMapSO)mapSO);
    }

    public static BurstMapSO Create(KopernicusMapSO mapSO)
    {
        var cpuTexture = mapSO.Texture;
        if (cpuTexture == null)
            return BurstMapSO.Create(new InvalidMapSO());

        var data = cpuTexture.GetRawTextureData<byte>();
        int w = cpuTexture.Width;
        int h = cpuTexture.Height;
        int mips = cpuTexture.MipCount;
        var depth = mapSO.Depth;

        // Kopernicus wraps R16+HeightAlpha textures in an RA16Texture adapter,
        // which doesn't correspond to a real TextureFormat. Check for it first.
        if (cpuTexture is KopernicusMapSO.RA16Texture)
        {
            return BurstMapSO.Create(
                new RA16(new CPUTexture2D.RA16(data, w, h, mips), depth)
            );
        }

        return cpuTexture.Format switch
        {
            TextureFormat.Alpha8 => BurstMapSO.Create(
                new Alpha8(new CPUTexture2D.Alpha8(data, w, h, mips), depth)
            ),
            TextureFormat.R8 => BurstMapSO.Create(
                new R8(new CPUTexture2D.R8(data, w, h, mips), depth)
            ),
            TextureFormat.R16 => BurstMapSO.Create(
                new R16(new CPUTexture2D.R16(data, w, h, mips), depth)
            ),
            TextureFormat.RG16 => BurstMapSO.Create(
                new RG16(new CPUTexture2D.RG16(data, w, h, mips), depth)
            ),
            TextureFormat.RGB24 => BurstMapSO.Create(
                new RGB24(new CPUTexture2D.RGB24(data, w, h, mips), depth)
            ),
            TextureFormat.RGB565 => BurstMapSO.Create(
                new RGB565(new CPUTexture2D.RGB565(data, w, h, mips), depth)
            ),
            TextureFormat.RGBA32 => BurstMapSO.Create(
                new RGBA32(new CPUTexture2D.RGBA32(data, w, h, mips), depth)
            ),
            TextureFormat.RGBA4444 => BurstMapSO.Create(
                new RGBA4444(new CPUTexture2D.RGBA4444(data, w, h, mips), depth)
            ),
            TextureFormat.ARGB32 => BurstMapSO.Create(
                new ARGB32(new CPUTexture2D.ARGB32(data, w, h, mips), depth)
            ),
            TextureFormat.ARGB4444 => BurstMapSO.Create(
                new ARGB4444(new CPUTexture2D.ARGB4444(data, w, h, mips), depth)
            ),
            TextureFormat.BGRA32 => BurstMapSO.Create(
                new BGRA32(new CPUTexture2D.BGRA32(data, w, h, mips), depth)
            ),
            TextureFormat.RFloat => BurstMapSO.Create(
                new RFloat(new CPUTexture2D.RFloat(data, w, h, mips), depth)
            ),
            TextureFormat.RGFloat => BurstMapSO.Create(
                new RGFloat(new CPUTexture2D.RGFloat(data, w, h, mips), depth)
            ),
            TextureFormat.RGBAFloat => BurstMapSO.Create(
                new RGBAFloat(new CPUTexture2D.RGBAFloat(data, w, h, mips), depth)
            ),
            TextureFormat.RHalf => BurstMapSO.Create(
                new RHalf(new CPUTexture2D.RHalf(data, w, h, mips), depth)
            ),
            TextureFormat.RGHalf => BurstMapSO.Create(
                new RGHalf(new CPUTexture2D.RGHalf(data, w, h, mips), depth)
            ),
            TextureFormat.RGBAHalf => BurstMapSO.Create(
                new RGBAHalf(new CPUTexture2D.RGBAHalf(data, w, h, mips), depth)
            ),
            TextureFormat.DXT1 => BurstMapSO.Create(
                new DXT1(new CPUTexture2D.DXT1(data, w, h, mips), depth)
            ),
            TextureFormat.DXT5 => BurstMapSO.Create(
                new DXT5(new CPUTexture2D.DXT5(data, w, h, mips), depth)
            ),
            TextureFormat.BC4 => BurstMapSO.Create(
                new BC4(new CPUTexture2D.BC4(data, w, h, mips), depth)
            ),
            TextureFormat.BC5 => BurstMapSO.Create(
                new BC5(new CPUTexture2D.BC5(data, w, h, mips), depth)
            ),
            TextureFormat.BC6H => BurstMapSO.Create(
                new BC6H(new CPUTexture2D.BC6H(data, w, h, mips), depth)
            ),
            TextureFormat.BC7 => BurstMapSO.Create(
                new BC7(new CPUTexture2D.BC7(data, w, h, mips), depth)
            ),
            _ => BurstMapSO.Create(new InvalidMapSO()),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetPixelFloat<T>(ref T texture, MapSO.MapDepth depth, int x, int y)
        where T : struct, ICPUTexture2D
    {
        Color c = texture.GetPixel(x, y);
        // Alpha8 stores its value in the alpha channel (r=g=b=1)
        if (texture.Format == TextureFormat.Alpha8)
            return c.a;
        return depth switch
        {
            MapSO.MapDepth.Greyscale => c.r,
            MapSO.MapDepth.HeightAlpha => (c.r + c.a) * 0.5f,
            MapSO.MapDepth.RGB => (c.r + c.g + c.b) * (1f / 3f),
            MapSO.MapDepth.RGBA => (c.r + c.g + c.b + c.a) * 0.25f,
            _ => 0f,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetPixelColor<T>(ref T texture, MapSO.MapDepth depth, int x, int y)
        where T : struct, ICPUTexture2D
    {
        Color c = texture.GetPixel(x, y);
        return depth switch
        {
            MapSO.MapDepth.Greyscale => texture.Format == TextureFormat.Alpha8
                ? new Color(c.a, c.a, c.a, 1f)
                : new Color(c.r, c.r, c.r, 1f),
            MapSO.MapDepth.HeightAlpha => new Color(c.r, c.r, c.r, c.a),
            _ => c,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetPixelColor32<T>(ref T texture, MapSO.MapDepth depth, int x, int y)
        where T : struct, ICPUTexture2D
    {
        Color32 c = texture.GetPixel32(x, y);
        return depth switch
        {
            MapSO.MapDepth.Greyscale => texture.Format == TextureFormat.Alpha8
                ? new Color32(c.a, c.a, c.a, 255)
                : new Color32(c.r, c.r, c.r, 255),
            MapSO.MapDepth.HeightAlpha => new Color32(c.r, c.r, c.r, c.a),
            _ => c,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HeightAlpha GetPixelHeightAlpha<T>(
        ref T texture,
        MapSO.MapDepth depth,
        int x,
        int y
    )
        where T : struct, ICPUTexture2D
    {
        Color c = texture.GetPixel(x, y);
        float height = texture.Format == TextureFormat.Alpha8 ? c.a : c.r;

        return depth switch
        {
            MapSO.MapDepth.HeightAlpha | MapSO.MapDepth.RGBA => new(height, c.a),
            _ => new(height, 1f),
        };
    }
}
