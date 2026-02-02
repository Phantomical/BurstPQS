using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BurstPQS.Map.Detail;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Map;

public struct HeightAlpha(float height, float alpha)
{
    public float height = height;
    public float alpha = alpha;

    public static implicit operator HeightAlpha(MapSO.HeightAlpha ha) => new(ha.height, ha.alpha);

    public static HeightAlpha Lerp(HeightAlpha a, HeightAlpha b, float dt)
    {
        return new HeightAlpha(
            a.height + (b.height - a.height) * dt,
            a.alpha + (b.alpha - a.alpha) * dt
        );
    }
}

/// <summary>
/// An interface for a <see cref="MapSO"/> that can be used with <see cref="BurstMapSO"/>.
/// </summary>
public interface IMapSO
    : IMapSO_GetPixelFloat_Int,
        IMapSO_GetPixelFloat_Float,
        IMapSO_GetPixelFloat_Double,
        IMapSO_GetPixelColor_Int,
        IMapSO_GetPixelColor_Float,
        IMapSO_GetPixelColor_Double,
        IMapSO_GetPixelColor32_Int,
        IMapSO_GetPixelColor32_Float,
        IMapSO_GetPixelColor32_Double,
        IMapSO_GetPixelHeightAlpha_Int,
        IMapSO_GetPixelHeightAlpha_Float,
        IMapSO_GetPixelHeightAlpha_Double
{
    // To make burst compilation convenient, these are defined in separate
    // interfaces with a [JobProducerType] attribute so that all the interface
    // methods will be burst-compiled if the [BurstCompile] attribute is
    // present.

    public int Height { get; }
    public int Width { get; }
}

public static class MapSODefaults
{
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

    static BilinearCoords<float> ConstructBilinearCoords<T>(ref T mapSO, float x, float y)
        where T : IMapSO
    {
        BilinearCoords<float> coords;
        x = Mathf.Abs(x - Mathf.Floor(x));
        y = Mathf.Abs(y - Mathf.Floor(y));

        coords.centerX = x * mapSO.Width;
        coords.centerY = y * mapSO.Height;

        coords.minX = Mathf.FloorToInt(coords.centerX);
        coords.maxX = Mathf.CeilToInt(coords.centerX);

        coords.minY = Mathf.FloorToInt(coords.centerY);
        coords.maxY = Mathf.CeilToInt(coords.centerY);

        coords.midX = coords.centerX - coords.minX;
        coords.midY = coords.centerY - coords.minY;

        if (coords.maxX == mapSO.Width)
            coords.maxX = 0;
        if (coords.maxY == mapSO.Height)
            coords.maxY = 0;

        return coords;
    }

    public static float GetPixelFloat<T>(ref T mapSO, float x, float y)
        where T : IMapSO
    {
        var c = ConstructBilinearCoords(ref mapSO, x, y);

        return Mathf.Lerp(
            Mathf.Lerp(
                mapSO.GetPixelFloat(c.minX, c.minY),
                mapSO.GetPixelFloat(c.maxX, c.minY),
                c.midX
            ),
            Mathf.Lerp(
                mapSO.GetPixelFloat(c.minX, c.maxY),
                mapSO.GetPixelFloat(c.maxX, c.maxY),
                c.midX
            ),
            c.midY
        );
    }

    public static float GetPixelFloat<T>(ref T mapSO, double x, double y)
        where T : IMapSO
    {
        return GetPixelFloat(ref mapSO, (float)x, (float)y);
    }

    public static Color GetPixelColor<T>(ref T mapSO, float x, float y)
        where T : IMapSO
    {
        var c = ConstructBilinearCoords(ref mapSO, x, y);
        return Color.Lerp(
            Color.Lerp(
                mapSO.GetPixelColor(c.minX, c.minY),
                mapSO.GetPixelColor(c.maxX, c.minY),
                c.midX
            ),
            Color.Lerp(
                mapSO.GetPixelColor(c.minX, c.maxY),
                mapSO.GetPixelColor(c.maxX, c.maxY),
                c.midX
            ),
            c.midY
        );
    }

    public static Color GetPixelColor<T>(ref T mapSO, double x, double y)
        where T : IMapSO
    {
        return GetPixelColor(ref mapSO, (float)x, (float)y);
    }

    public static Color32 GetPixelColor32<T>(ref T mapSO, float x, float y)
        where T : IMapSO
    {
        var c = ConstructBilinearCoords(ref mapSO, x, y);
        return Color32.Lerp(
            Color32.Lerp(
                mapSO.GetPixelColor32(c.minX, c.minY),
                mapSO.GetPixelColor32(c.maxX, c.minY),
                c.midX
            ),
            Color32.Lerp(
                mapSO.GetPixelColor32(c.minX, c.maxY),
                mapSO.GetPixelColor32(c.maxX, c.maxY),
                c.midX
            ),
            c.midY
        );
    }

    public static Color32 GetPixelColor32<T>(ref T mapSO, double x, double y)
        where T : IMapSO
    {
        return GetPixelColor(ref mapSO, (float)x, (float)y);
    }

    public static HeightAlpha GetPixelHeightAlpha<T>(ref T mapSO, float x, float y)
        where T : IMapSO
    {
        var c = ConstructBilinearCoords(ref mapSO, x, y);
        return HeightAlpha.Lerp(
            HeightAlpha.Lerp(
                mapSO.GetPixelHeightAlpha(c.minX, c.minY),
                mapSO.GetPixelHeightAlpha(c.maxX, c.minY),
                c.midX
            ),
            HeightAlpha.Lerp(
                mapSO.GetPixelHeightAlpha(c.minX, c.maxY),
                mapSO.GetPixelHeightAlpha(c.maxX, c.maxY),
                c.midX
            ),
            c.midY
        );
    }

    public static HeightAlpha GetPixelHeightAlpha<T>(ref T mapSO, double x, double y)
        where T : IMapSO
    {
        return GetPixelHeightAlpha(ref mapSO, (float)x, (float)y);
    }
}

internal unsafe struct MapSOVTable
{
    internal delegate void DisposeFn(void* self);

    internal delegate float GetPixelFloat_Int_Fn(void* self, int x, int y);
    internal delegate float GetPixelFloat_Float_Fn(void* self, float x, float y);
    internal delegate float GetPixelFloat_Double_Fn(void* self, double x, double y);
    internal delegate void GetPixelColor_Int_Fn(void* self, int x, int y, out Color color);
    internal delegate void GetPixelColor_Float_Fn(void* self, float x, float y, out Color color);
    internal delegate void GetPixelColor_Double_Fn(void* self, double x, double y, out Color color);
    internal delegate void GetPixelColor32_Int_Fn(void* self, int x, int y, out Color32 color);
    internal delegate void GetPixelColor32_Float_Fn(
        void* self,
        float x,
        float y,
        out Color32 color
    );
    internal delegate void GetPixelColor32_Double_Fn(
        void* self,
        double x,
        double y,
        out Color32 color
    );
    internal delegate void GetPixelHeightAlpha_Int_Fn(void* self, int x, int y, out HeightAlpha ha);
    internal delegate void GetPixelHeightAlpha_Float_Fn(
        void* self,
        float x,
        float y,
        out HeightAlpha ha
    );
    internal delegate void GetPixelHeightAlpha_Double_Fn(
        void* self,
        double x,
        double y,
        out HeightAlpha ha
    );

    FunctionPointer<GetPixelFloat_Int_Fn> GetPixelFloat_Int_Fp;
    FunctionPointer<GetPixelFloat_Float_Fn> GetPixelFloat_Float_Fp;
    FunctionPointer<GetPixelFloat_Double_Fn> GetPixelFloat_Double_Fp;

    FunctionPointer<GetPixelColor_Int_Fn> GetPixelColor_Int_Fp;
    FunctionPointer<GetPixelColor_Float_Fn> GetPixelColor_Float_Fp;
    FunctionPointer<GetPixelColor_Double_Fn> GetPixelColor_Double_Fp;

    FunctionPointer<GetPixelColor32_Int_Fn> GetPixelColor32_Int_Fp;
    FunctionPointer<GetPixelColor32_Float_Fn> GetPixelColor32_Float_Fp;
    FunctionPointer<GetPixelColor32_Double_Fn> GetPixelColor32_Double_Fp;

    FunctionPointer<GetPixelHeightAlpha_Int_Fn> GetPixelHeightAlpha_Int_Fp;
    FunctionPointer<GetPixelHeightAlpha_Float_Fn> GetPixelHeightAlpha_Float_Fp;
    FunctionPointer<GetPixelHeightAlpha_Double_Fn> GetPixelHeightAlpha_Double_Fp;

    FunctionPointer<DisposeFn> Dispose_Fp;

    public static MapSOVTable Create<T>()
        where T : struct, IMapSO
    {
        return new()
        {
            GetPixelFloat_Int_Fp = MapSOVTable<T>.GetPixelFloat_Int_Fp,
            GetPixelFloat_Float_Fp = MapSOVTable<T>.GetPixelFloat_Float_Fp,
            GetPixelFloat_Double_Fp = MapSOVTable<T>.GetPixelFloat_Double_Fp,
            GetPixelColor_Int_Fp = MapSOVTable<T>.GetPixelColor_Int_Fp,
            GetPixelColor_Float_Fp = MapSOVTable<T>.GetPixelColor_Float_Fp,
            GetPixelColor_Double_Fp = MapSOVTable<T>.GetPixelColor_Double_Fp,
            GetPixelColor32_Int_Fp = MapSOVTable<T>.GetPixelColor32_Int_Fp,
            GetPixelColor32_Float_Fp = MapSOVTable<T>.GetPixelColor32_Float_Fp,
            GetPixelColor32_Double_Fp = MapSOVTable<T>.GetPixelColor32_Double_Fp,
            GetPixelHeightAlpha_Int_Fp = MapSOVTable<T>.GetPixelHeightAlpha_Int_Fp,
            GetPixelHeightAlpha_Float_Fp = MapSOVTable<T>.GetPixelHeightAlpha_Float_Fp,
            GetPixelHeightAlpha_Double_Fp = MapSOVTable<T>.GetPixelHeightAlpha_Double_Fp,
            Dispose_Fp = MapSOVTable<T>.Dispose_Fp,
        };
    }

    public readonly float GetPixelFloat(void* mapSO, int x, int y) =>
        GetPixelFloat_Int_Fp.Invoke(mapSO, x, y);

    public readonly float GetPixelFloat(void* mapSO, float x, float y) =>
        GetPixelFloat_Float_Fp.Invoke(mapSO, x, y);

    public readonly float GetPixelFloat(void* mapSO, double x, double y) =>
        GetPixelFloat_Double_Fp.Invoke(mapSO, x, y);

    public readonly Color GetPixelColor(void* mapSO, int x, int y)
    {
        GetPixelColor_Int_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly Color GetPixelColor(void* mapSO, float x, float y)
    {
        GetPixelColor_Float_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly Color GetPixelColor(void* mapSO, double x, double y)
    {
        GetPixelColor_Double_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly Color32 GetPixelColor32(void* mapSO, int x, int y)
    {
        GetPixelColor32_Int_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly Color32 GetPixelColor32(void* mapSO, float x, float y)
    {
        GetPixelColor32_Float_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly Color32 GetPixelColor32(void* mapSO, double x, double y)
    {
        GetPixelColor32_Double_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly HeightAlpha GetPixelHeightAlpha(void* mapSO, int x, int y)
    {
        GetPixelHeightAlpha_Int_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly HeightAlpha GetPixelHeightAlpha(void* mapSO, float x, float y)
    {
        GetPixelHeightAlpha_Float_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly HeightAlpha GetPixelHeightAlpha(void* mapSO, double x, double y)
    {
        GetPixelHeightAlpha_Double_Fp.Invoke(mapSO, x, y, out var color);
        return color;
    }

    public readonly void Dispose(void* mapSO)
    {
        if (Dispose_Fp.IsCreated)
            Dispose_Fp.Invoke(mapSO);
    }
}

internal static unsafe class MapSOVTable<T>
    where T : struct, IMapSO
{
    internal class Container(T mapSO)
    {
        public T mapSO = mapSO;
    }

    internal static readonly int FieldOffset = UnsafeUtility.GetFieldOffset(
        typeof(Container).GetField("mapSO")
    );

    internal static readonly FunctionPointer<MapSOVTable.GetPixelFloat_Int_Fn> GetPixelFloat_Int_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelFloat_Float_Fn> GetPixelFloat_Float_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelFloat_Double_Fn> GetPixelFloat_Double_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelColor_Int_Fn> GetPixelColor_Int_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelColor_Float_Fn> GetPixelColor_Float_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelColor_Double_Fn> GetPixelColor_Double_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelColor32_Int_Fn> GetPixelColor32_Int_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelColor32_Float_Fn> GetPixelColor32_Float_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelColor32_Double_Fn> GetPixelColor32_Double_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelHeightAlpha_Int_Fn> GetPixelHeightAlpha_Int_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelHeightAlpha_Float_Fn> GetPixelHeightAlpha_Float_Fp;
    internal static readonly FunctionPointer<MapSOVTable.GetPixelHeightAlpha_Double_Fn> GetPixelHeightAlpha_Double_Fp;
    internal static readonly FunctionPointer<MapSOVTable.DisposeFn> Dispose_Fp;

    internal static readonly MapSOVTable VTable;

    static MapSOVTable()
    {
        GetPixelFloat_Int_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelFloat_Int_Fn>(
                MapSOExtensions.GetPixelFloat_Int<T>.Execute
            );
        GetPixelFloat_Float_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelFloat_Float_Fn>(
                MapSOExtensions.GetPixelFloat_Float<T>.Execute
            );
        GetPixelFloat_Double_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelFloat_Double_Fn>(
                MapSOExtensions.GetPixelFloat_Double<T>.Execute
            );

        GetPixelColor_Int_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelColor_Int_Fn>(
                MapSOExtensions.GetPixelColor_Int<T>.Execute
            );
        GetPixelColor_Float_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelColor_Float_Fn>(
                MapSOExtensions.GetPixelColor_Float<T>.Execute
            );
        GetPixelColor_Double_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelColor_Double_Fn>(
                MapSOExtensions.GetPixelColor_Double<T>.Execute
            );

        GetPixelColor32_Int_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelColor32_Int_Fn>(
                MapSOExtensions.GetPixelColor32_Int<T>.Execute
            );
        GetPixelColor32_Float_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelColor32_Float_Fn>(
                MapSOExtensions.GetPixelColor32_Float<T>.Execute
            );
        GetPixelColor32_Double_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelColor32_Double_Fn>(
                MapSOExtensions.GetPixelColor32_Double<T>.Execute
            );

        GetPixelHeightAlpha_Int_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelHeightAlpha_Int_Fn>(
                MapSOExtensions.GetPixelHeightAlpha_Int<T>.Execute
            );
        GetPixelHeightAlpha_Float_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelHeightAlpha_Float_Fn>(
                MapSOExtensions.GetPixelHeightAlpha_Float<T>.Execute
            );
        GetPixelHeightAlpha_Double_Fp =
            BurstUtil.MaybeCompileFunctionPointer<MapSOVTable.GetPixelHeightAlpha_Double_Fn>(
                MapSOExtensions.GetPixelHeightAlpha_Double<T>.Execute
            );

        if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
        {
            var disposeFn = typeof(MapSOExtensions)
                .GetMethod("Dispose")
                .MakeGenericMethod(typeof(T));
            var del = (MapSOVTable.DisposeFn)
                Delegate.CreateDelegate(typeof(MapSOVTable.DisposeFn), disposeFn);

            Dispose_Fp = BurstUtil.MaybeCompileFunctionPointer(del);
        }

        VTable = MapSOVTable.Create<T>();
    }
}

/// <summary>
/// A wrapper around a <see cref="MapSO"/> that can be used from burst-compiled
/// code. This struct takes care of all the indirection needed to support multiple
/// different <see cref="MapSO"/> types.
/// </summary>
///
/// <remarks>
/// <para>
/// To add support for your own <see cref="MapSO"/> type you will need to create an
/// adapter struct that implements <see cref="IMapSO"/> and optionally
/// <see cref="IDisposable"/>. Once you have done this you can register a factory
/// function by calling <see cref="RegisterMapSOFactoryFunc{TMapSO}(Func{TMapSO, BurstMapSO})"/>,
/// and it will be used transparently from then on out when encountered.
/// </para>
///
/// <para>
/// You can also burst-compile your adapter functions by adding a <c>[BurstCompile]</c>
/// attribute on the struct itself. This will automatically compile any of the
/// <c>GetPixel</c> functions, though not <c>Dispose</c>.
/// </para>
/// </remarks>
public unsafe struct BurstMapSO : IMapSO, IDisposable
{
    void* data;
    MapSOVTable* vtable;
    ulong gchandle;

    int width;
    int height;

    public readonly bool IsValid => data is not null;
    public readonly int Width => width;
    public readonly int Height => height;

    public static BurstMapSO Create<T>(T mapSO)
        where T : struct, IMapSO
    {
        var width = mapSO.Width;
        var height = mapSO.Height;
        var container = new MapSOVTable<T>.Container(mapSO);

        // This is probably wildly unsafe, but mono never moves static readonly fields
        // so it is ok
        var vtable = (MapSOVTable*)Unsafe.AsPointer(ref Unsafe.AsRef(in MapSOVTable<T>.VTable));
        var obj = UnsafeUtility.PinGCObjectAndGetAddress(container, out var gchandle);
        var data = Unsafe.Add<byte>(obj, MapSOVTable<T>.FieldOffset);

        return new()
        {
            data = data,
            vtable = vtable,
            gchandle = gchandle,
            width = width,
            height = height,
        };
    }

    public static BurstMapSO Create(global::MapSO mapSO)
    {
        var factory = BurstMapSORegistry.GetFactoryFunc(mapSO);
        return factory(mapSO);
    }

    public static void RegisterMapSOFactoryFunc<TMapSO>(Func<TMapSO, BurstMapSO> func)
        where TMapSO : global::MapSO
    {
        BurstMapSORegistry.RegisterFunc(func);
    }

    public void Dispose()
    {
        if (data is null)
            return;

        vtable->Dispose(data);

        UnsafeUtility.ReleaseGCObject(gchandle);
        this = default;
    }

    public float GetPixelFloat(int x, int y)
    {
        if (!IsValid)
            return 0f;

        return vtable->GetPixelFloat(data, x, y);
    }

    public float GetPixelFloat(float x, float y)
    {
        if (!IsValid)
            return 0f;

        return vtable->GetPixelFloat(data, x, y);
    }

    public float GetPixelFloat(double x, double y)
    {
        if (!IsValid)
            return 0f;

        return vtable->GetPixelFloat(data, x, y);
    }

    public Color GetPixelColor(int x, int y)
    {
        if (!IsValid)
            return Color.black;

        return vtable->GetPixelColor(data, x, y);
    }

    public Color GetPixelColor(float x, float y)
    {
        if (!IsValid)
            return Color.black;

        return vtable->GetPixelColor(data, x, y);
    }

    public Color GetPixelColor(double x, double y)
    {
        if (!IsValid)
            return Color.black;

        return vtable->GetPixelColor(data, x, y);
    }

    public Color32 GetPixelColor32(int x, int y)
    {
        if (!IsValid)
            return default;

        return vtable->GetPixelColor32(data, x, y);
    }

    public Color32 GetPixelColor32(float x, float y)
    {
        if (!IsValid)
            return default;

        return vtable->GetPixelColor32(data, x, y);
    }

    public Color32 GetPixelColor32(double x, double y)
    {
        if (!IsValid)
            return default;

        return vtable->GetPixelColor32(data, x, y);
    }

    public HeightAlpha GetPixelHeightAlpha(int x, int y)
    {
        if (!IsValid)
            return default;

        return vtable->GetPixelHeightAlpha(data, x, y);
    }

    public HeightAlpha GetPixelHeightAlpha(float x, float y)
    {
        if (!IsValid)
            return default;

        return vtable->GetPixelHeightAlpha(data, x, y);
    }

    public HeightAlpha GetPixelHeightAlpha(double x, double y)
    {
        if (!IsValid)
            return default;

        return vtable->GetPixelHeightAlpha(data, x, y);
    }
}

internal class BurstMapSORegistry
{
    static readonly Dictionary<Type, Func<global::MapSO, BurstMapSO>> Registry = [];

    internal static void RegisterFunc<TMapSO>(Func<TMapSO, BurstMapSO> func)
        where TMapSO : global::MapSO
    {
        if (Registry.ContainsKey(typeof(TMapSO)))
            throw new Exception(
                $"there is already a BurstMapSO function registered for type `{typeof(TMapSO).Name}"
            );

        Registry.Add(typeof(TMapSO), mapSO => func((TMapSO)mapSO));
    }

    internal static Func<global::MapSO, BurstMapSO> GetFactoryFunc(global::MapSO mapSO)
    {
        if (!Registry.TryGetValue(mapSO.GetType(), out var func))
        {
            Debug.LogError(
                $"No BurstMapSO factory function registered for MapSO type {mapSO.GetType().Name}"
            );
            return CreateEmptyMapSO;
        }

        return func;
    }

    static BurstMapSO CreateEmptyMapSO(global::MapSO _) => new();
}
