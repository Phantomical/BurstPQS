using System;
using BurstPQS.Collections;
using KSP.UI;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Util;

public struct BurstMapSO
{
    public struct HeightAlpha(float height, float alpha)
    {
        public float height = height;
        public float alpha = alpha;

        public HeightAlpha()
            : this(0f, 0f) { }

        public static HeightAlpha Lerp(HeightAlpha a, HeightAlpha b, float dt)
        {
            return new HeightAlpha(
                a.height + (b.height - a.height) * dt,
                a.alpha + (b.alpha - a.alpha) * dt
            );
        }
    }

    struct BilinearCoords<T>
    {
        public int minX;
        public int maxX;
        public T midX;
        public T centerX;

        public int minY;
        public int maxY;
        public T midY;
        public T centerY;
    }

    public struct MapSOGuard : IDisposable
    {
        internal ulong handle;

        public void Dispose()
        {
            UnsafeUtility.ReleaseGCObject(handle);
            handle = 0;
        }
    }

    const float Byte2Float = 0.003921569f;
    const float Float2Byte = 255f;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int BitsPerPixel { get; private set; }
    public int RowWidth { get; private set; }
    public readonly MapSO.MapDepth Depth => (MapSO.MapDepth)BitsPerPixel;
    public readonly int Size => data.Length;

    MemorySpan<byte> data;

    private BurstMapSO(MapSO mapSO, MemorySpan<byte> data)
    {
        Width = mapSO.Width;
        Height = mapSO.Height;
        BitsPerPixel = mapSO.BitsPerPixel;
        RowWidth = mapSO.RowWidth;
        this.data = data;
    }

    /// <summary>
    /// Create a new <see cref="BurstMapSO"/> struct that can be used to access
    /// the provided <see cref="MapSO"/> object from within burst.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// Make sure to dispose of the returned <see cref="MapSOGuard"/> once you
    /// are done with the <see cref="BurstMapSO"/>. If you don't, then the map
    /// data will be leaked.
    /// </para>
    ///
    /// <para>
    /// The recommended patttern to do this is:
    /// <code>
    /// using var guard = BurstMapSO.Create(mapSO, out var burstMapSO);
    /// </code>
    /// </para>
    /// </remarks>
    public static unsafe MapSOGuard Create(MapSO mapSO, out BurstMapSO burst)
    {
        if (mapSO.GetType() != typeof(MapSO))
            throw new InvalidOperationException(
                "Cannot use BurstMapSO with derived subclasses of MapSO"
            );

        var data = UnsafeUtility.PinGCArrayAndGetDataAddress(mapSO._data, out var handle);
        var guard = new MapSOGuard { handle = handle };
        var span = new MemorySpan<byte>((byte*)data, mapSO._data.Length);
        burst = new(mapSO, span);

        return guard;
    }

    readonly BilinearCoords<float> ConstructBilinearCoords(float x, float y)
    {
        BilinearCoords<float> coords;
        x = Mathf.Abs(x - Mathf.Floor(x));
        y = Mathf.Abs(y - Mathf.Floor(y));

        coords.centerX = x * Width;
        coords.centerY = y * Height;

        coords.minX = Mathf.FloorToInt(coords.centerX);
        coords.maxX = Mathf.CeilToInt(coords.centerX);

        coords.minY = Mathf.FloorToInt(coords.centerY);
        coords.maxY = Mathf.CeilToInt(coords.centerY);

        coords.midX = coords.centerX - coords.minX;
        coords.midY = coords.centerY - coords.minY;

        if (coords.maxX == Width)
            coords.maxX = 0;
        if (coords.maxY == Height)
            coords.maxY = 0;

        return coords;
    }

    readonly int PixelIndex(int x, int y)
    {
        return x * BitsPerPixel + y * RowWidth;
    }

    public readonly float GreyFloat(int x, int y)
    {
        return Byte2Float * data[PixelIndex(x, y)];
    }

    public readonly byte GetPixelByte(int x, int y)
    {
        if (x < 0)
            x = Width - x;
        else if (x >= Width)
            x -= Width;

        if (y < 0)
            y = Height - y;
        else if (y >= Height)
            y -= Height;

        return data[PixelIndex(x, y)];
    }

    public readonly float GetPixelFloat(int x, int y)
    {
        var ret = 0f;
        var index = PixelIndex(x, y);

        for (int i = 0; i < BitsPerPixel; ++i)
            ret += data[index + i];

        ret /= BitsPerPixel;
        ret *= Byte2Float;
        return ret;
    }

    public readonly Color GetPixelColor(int x, int y)
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

    public readonly Color32 GetPixelColor32(int x, int y)
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

    public readonly HeightAlpha GetPixelHeightAlpha(int x, int y)
    {
        var index = PixelIndex(x, y);

        return BitsPerPixel switch
        {
            4 => new(Byte2Float * data[index], Byte2Float * data[index + 3]),
            2 => new(Byte2Float * data[index], Byte2Float * data[index + 1]),
            _ => new(Byte2Float * data[index], 1f),
        };
    }

    public readonly Color GetPixelColor(float x, float y)
    {
        var c = ConstructBilinearCoords(x, y);
        return Color.Lerp(
            Color.Lerp(GetPixelColor(c.minX, c.minY), GetPixelColor(c.maxX, c.minY), c.midX),
            Color.Lerp(GetPixelColor(c.minX, c.maxY), GetPixelColor(c.maxX, c.maxY), c.midX),
            c.midY
        );
    }

    public readonly float GetPixelFloat(float x, float y)
    {
        var c = ConstructBilinearCoords(x, y);
        if (BitsPerPixel == 1)
        {
            return Mathf.Lerp(
                Mathf.Lerp(GreyFloat(c.minX, c.minY), GreyFloat(c.maxX, c.minY), c.midX),
                Mathf.Lerp(GreyFloat(c.minX, c.maxY), GreyFloat(c.maxX, c.maxY), c.midX),
                c.midY
            );
        }
        else
        {
            return Mathf.Lerp(
                Mathf.Lerp(GetPixelFloat(c.minX, c.minY), GetPixelFloat(c.maxX, c.minY), c.midX),
                Mathf.Lerp(GetPixelFloat(c.minX, c.maxY), GetPixelFloat(c.maxX, c.maxY), c.midX),
                c.midY
            );
        }
    }

    public readonly HeightAlpha GetPixelHeightAlpha(float x, float y)
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

    public readonly Color GetPixelColor(double x, double y) => GetPixelColor((float)x, (float)y);

    public readonly float GetPixelFloat(double x, double y) => GetPixelFloat((float)x, (float)y);

    public readonly HeightAlpha GetPixelHeightAlpha(double x, double y) =>
        GetPixelHeightAlpha((float)x, (float)y);
}
