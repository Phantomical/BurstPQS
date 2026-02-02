using System;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Map;

/// <summary>
/// An <see cref="IMapSO"/> implementation for a stock <see cref="MapSO"/>.
/// </summary>
[BurstCompile(FloatMode = FloatMode.Fast)]
public struct StockBurstMapSO : IMapSO, IDisposable
{
    const float Byte2Float = 0.003921569f;

    NativeArray<byte> data;
    ulong gchandle;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int BitsPerPixel { get; private set; }
    public int RowWidth { get; private set; }

    public readonly MapSO.MapDepth Depth => (MapSO.MapDepth)BitsPerPixel;
    public readonly int Size => data.Length;

    public unsafe StockBurstMapSO(MapSO mapSO)
    {
        Width = mapSO.Width;
        Height = mapSO.Height;
        BitsPerPixel = mapSO.BitsPerPixel;
        RowWidth = mapSO.RowWidth;

        var dataptr = UnsafeUtility.PinGCArrayAndGetDataAddress(mapSO._data, out gchandle);
        data = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
            dataptr,
            mapSO._data.Length,
            Allocator.Invalid
        );
    }

    readonly int PixelIndex(int x, int y)
    {
        x = MathUtil.Clamp(x, 0, Width);
        y = MathUtil.Clamp(y, 0, Height);

        var index = y * RowWidth + x * BitsPerPixel;
        if (index < 0 || index > Size)
            index = 0;
        return index;
    }

    public float GetPixelFloat(int x, int y)
    {
        var ret = 0f;
        var index = PixelIndex(x, y);

        for (int i = 0; i < BitsPerPixel; ++i)
            ret += data[index + i];

        ret /= BitsPerPixel;
        ret *= Byte2Float;
        return ret;
    }

    public Color GetPixelColor(int x, int y)
    {
        var index = PixelIndex(x, y);
        float val;

        switch (BitsPerPixel)
        {
            case 4:
                return new(
                    Byte2Float * data[index],
                    Byte2Float * data[index + 1],
                    Byte2Float * data[index + 2],
                    Byte2Float * data[index + 3]
                );
            case 3:
                return new(
                    Byte2Float * data[index],
                    Byte2Float * data[index + 1],
                    Byte2Float * data[index + 2],
                    1f
                );
            case 2:
                val = Byte2Float * data[index];
                return new(val, val, val, Byte2Float * data[index + 1]);
            case 1:
            default:
                val = Byte2Float * data[index];
                return new(val, val, val, 1f);
        }
    }

    public Color32 GetPixelColor32(int x, int y)
    {
        var index = PixelIndex(x, y);
        byte val;

        switch (BitsPerPixel)
        {
            case 4:
                return new(data[index], data[index + 1], data[index + 2], data[index + 3]);
            case 3:
                return new(data[index], data[index + 1], data[index + 2], byte.MaxValue);
            case 2:
                val = data[index];
                return new Color(val, val, val, data[index + 1]); // this looks like a bug in the KSP source
            case 1:
            default:
                val = data[index];
                return new(val, val, val, byte.MaxValue);
        }
    }

    public HeightAlpha GetPixelHeightAlpha(int x, int y)
    {
        var index = PixelIndex(x, y);

        return BitsPerPixel switch
        {
            4 => new(Byte2Float * data[index], Byte2Float * data[index + 3]),
            2 => new(Byte2Float * data[index], Byte2Float * data[index + 1]),
            _ => new(Byte2Float * data[index], 1f),
        };
    }

    public Color GetPixelColor(float x, float y) => MapSODefaults.GetPixelColor(ref this, x, y);

    public Color GetPixelColor(double x, double y) => MapSODefaults.GetPixelColor(ref this, x, y);

    public Color32 GetPixelColor32(float x, float y) =>
        MapSODefaults.GetPixelColor32(ref this, x, y);

    public Color32 GetPixelColor32(double x, double y) =>
        MapSODefaults.GetPixelColor32(ref this, x, y);

    public float GetPixelFloat(float x, float y) => MapSODefaults.GetPixelFloat(ref this, x, y);

    public float GetPixelFloat(double x, double y) => MapSODefaults.GetPixelFloat(ref this, x, y);

    public HeightAlpha GetPixelHeightAlpha(float x, float y) =>
        MapSODefaults.GetPixelHeightAlpha(ref this, x, y);

    public HeightAlpha GetPixelHeightAlpha(double x, double y) =>
        MapSODefaults.GetPixelHeightAlpha(ref this, x, y);

    public void Dispose()
    {
        UnsafeUtility.ReleaseGCObject(gchandle);
        data = default;
    }
}
