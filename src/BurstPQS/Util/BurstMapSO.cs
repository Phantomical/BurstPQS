using System;
using System.Runtime.InteropServices;
using BurstPQS.Collections;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BurstPQS.Util;

public readonly unsafe struct BurstMapSO : IBurstMapSO
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

    public struct Guard : IDisposable
    {
        internal ulong gcHandle;
        internal void* alloc;

        public readonly void Dispose()
        {
            UnsafeUtility.ReleaseGCObject(gcHandle);
            if (alloc is not null)
                UnsafeUtility.Free(alloc, Allocator.Temp);
        }
    }

    readonly MapSOType type;
    readonly void* mapSO;

    private BurstMapSO(void* mapSO, MapSOType type)
    {
        this.mapSO = mapSO;
        this.type = type;
    }

    public static Guard Create(MapSO mapSO, out BurstMapSO burst)
    {
        ulong gcHandle;
        void* ptr;
        MapSOType type;
        if (mapSO.GetType() == typeof(MapSO))
        {
            var data = UnsafeUtility.PinGCArrayAndGetDataAddress(mapSO._data, out gcHandle);
            var span = new MemorySpan<byte>((byte*)data, mapSO._data.Length);
            ptr = BurstUtil.Alloc(new PlainMapSO(mapSO, span));
            type = MapSOType.Plain;
        }
        else
        {
            ptr = BurstUtil.Alloc(
                new GenericMapSO(
                    (MapSO*)UnsafeUtility.PinGCObjectAndGetAddress(mapSO, out gcHandle)
                )
            );
            type = MapSOType.Generic;
        }

        burst = new(ptr, type);
        return new() { gcHandle = gcHandle, alloc = ptr };
    }

    delegate void GetPixelColorDelegate(MapSO* gcHandle, float x, float y, out Color color);
    delegate void GetPixelColor32Delegate(MapSO* gcHandle, float x, float y, out Color32 color);
    delegate float GetPixelFloatDelegate(MapSO* gcHandle, float x, float y);
    delegate void GetPixelHeightAlphaDelegate(
        MapSO* gcHandle,
        float x,
        float y,
        out HeightAlpha value
    );

    // This needs to be in a separate class so that burst doesn't try to evaluate
    // its static constructor.
    static class Functions
    {
        public static readonly FunctionPointer<GetPixelColorDelegate> GetPixelColorFp;
        public static readonly FunctionPointer<GetPixelColor32Delegate> GetPixelColor32Fp;
        public static readonly FunctionPointer<GetPixelFloatDelegate> GetPixelFloatFp;
        public static readonly FunctionPointer<GetPixelHeightAlphaDelegate> GetPixelHeightAlphaFp;

        static void GetPixelColor(MapSO* mapSO, float x, float y, out Color color) =>
            color = mapSO->GetPixelColor(x, y);

        static void GetPixelColor32(MapSO* mapSO, float x, float y, out Color32 color) =>
            color = mapSO->GetPixelColor32(x, y);

        static float GetPixelFloat(MapSO* mapSO, float x, float y) => mapSO->GetPixelFloat(x, y);

        static void GetPixelHeightAlpha(
            MapSO* mapSO,
            float x,
            float y,
            out BurstMapSO.HeightAlpha ha
        )
        {
            var heightAlpha = mapSO->GetPixelHeightAlpha(x, y);
            ha = new(heightAlpha.height, heightAlpha.alpha);
        }

        static Functions()
        {
            GetPixelColorFp = new(
                Marshal.GetFunctionPointerForDelegate<GetPixelColorDelegate>(GetPixelColor)
            );
            GetPixelColor32Fp = new(
                Marshal.GetFunctionPointerForDelegate<GetPixelColor32Delegate>(GetPixelColor32)
            );
            GetPixelFloatFp = new(
                Marshal.GetFunctionPointerForDelegate<GetPixelFloatDelegate>(GetPixelFloat)
            );
            GetPixelHeightAlphaFp = new(
                Marshal.GetFunctionPointerForDelegate<GetPixelHeightAlphaDelegate>(
                    GetPixelHeightAlpha
                )
            );
        }
    }

    static T Unreachable<T>()
    {
        Hint.Assume(false);
        return default;
    }

    public readonly Color GetPixelColor(float x, float y)
    {
        return type switch
        {
            MapSOType.Generic => ((GenericMapSO*)mapSO)->GetPixelColor(x, y),
            MapSOType.Plain => ((PlainMapSO*)mapSO)->GetPixelColor(x, y),
            _ => Unreachable<Color>(),
        };
    }

    public readonly Color32 GetPixelColor32(float x, float y)
    {
        return type switch
        {
            MapSOType.Generic => ((GenericMapSO*)mapSO)->GetPixelColor32(x, y),
            MapSOType.Plain => ((PlainMapSO*)mapSO)->GetPixelColor32(x, y),
            _ => Unreachable<Color32>(),
        };
    }

    public readonly float GetPixelFloat(float x, float y)
    {
        return type switch
        {
            MapSOType.Generic => ((GenericMapSO*)mapSO)->GetPixelFloat(x, y),
            MapSOType.Plain => ((PlainMapSO*)mapSO)->GetPixelFloat(x, y),
            _ => Unreachable<float>(),
        };
    }

    public readonly HeightAlpha GetPixelHeightAlpha(float x, float y)
    {
        return type switch
        {
            MapSOType.Generic => ((GenericMapSO*)mapSO)->GetPixelHeightAlpha(x, y),
            MapSOType.Plain => ((PlainMapSO*)mapSO)->GetPixelHeightAlpha(x, y),
            _ => Unreachable<HeightAlpha>(),
        };
    }

    public readonly Color GetPixelColor(double x, double y) => GetPixelColor((float)x, (float)y);

    public readonly float GetPixelFloat(double x, double y) => GetPixelFloat((float)x, (float)y);

    public readonly Color32 GetPixelColor32(double x, double y) =>
        GetPixelColor32((float)x, (float)y);

    public readonly HeightAlpha GetPixelHeightAlpha(double x, double y) =>
        GetPixelHeightAlpha((float)x, (float)y);

    enum MapSOType
    {
        Generic,
        Plain,
    }

    struct GenericMapSO(MapSO* mapSO)
    {
        public MapSO* mapSO = mapSO;

        readonly FunctionPointer<GetPixelColorDelegate> GetPixelColorFp = Functions.GetPixelColorFp;
        readonly FunctionPointer<GetPixelColor32Delegate> GetPixelColor32Fp =
            Functions.GetPixelColor32Fp;
        readonly FunctionPointer<GetPixelFloatDelegate> GetPixelFloatFp = Functions.GetPixelFloatFp;
        readonly FunctionPointer<GetPixelHeightAlphaDelegate> GetPixelHeightAlphaFp =
            Functions.GetPixelHeightAlphaFp;

        public readonly float GetPixelFloat(float x, float y) =>
            GetPixelFloatFp.Invoke(mapSO, x, y);

        public readonly Color GetPixelColor(float x, float y)
        {
            GetPixelColorFp.Invoke(mapSO, x, y, out var color);
            return color;
        }

        public readonly Color32 GetPixelColor32(float x, float y)
        {
            GetPixelColor32Fp.Invoke(mapSO, x, y, out var color);
            return color;
        }

        public readonly HeightAlpha GetPixelHeightAlpha(float x, float y)
        {
            GetPixelHeightAlphaFp.Invoke(mapSO, x, y, out var ha);
            return ha;
        }
    }

    struct PlainMapSO(MapSO mapSO, ReadOnlyMemorySpan<byte> data)
    {
        const float Byte2Float = 0.003921569f;
        const float Float2Byte = 255f;

        public int Width { get; private set; } = mapSO.Width;
        public int Height { get; private set; } = mapSO.Height;
        public int BitsPerPixel { get; private set; } = mapSO.BitsPerPixel;
        public int RowWidth { get; private set; } = mapSO.RowWidth;
        public readonly MapSO.MapDepth Depth => (MapSO.MapDepth)BitsPerPixel;
        public readonly int Size => data.Length;

        readonly ReadOnlyMemorySpan<byte> data = data;

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

        public readonly Color32 GetPixelColor32(float x, float y)
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
                    Mathf.Lerp(
                        GetPixelFloat(c.minX, c.minY),
                        GetPixelFloat(c.maxX, c.minY),
                        c.midX
                    ),
                    Mathf.Lerp(
                        GetPixelFloat(c.minX, c.maxY),
                        GetPixelFloat(c.maxX, c.maxY),
                        c.midX
                    ),
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
}
