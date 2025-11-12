using System;
using System.Runtime.InteropServices;
using Unity.Burst;
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

        public readonly void Dispose()
        {
            UnsafeUtility.ReleaseGCObject(gcHandle);
        }
    }

    readonly MapSO* mapSO;

    readonly FunctionPointer<GetPixelColorDelegate> GetPixelColorFp;
    readonly FunctionPointer<GetPixelColor32Delegate> GetPixelColor32Fp;
    readonly FunctionPointer<GetPixelFloatDelegate> GetPixelFloatFp;
    readonly FunctionPointer<GetPixelHeightAlphaDelegate> GetPixelHeightAlphaFp;

    private BurstMapSO(MapSO* mapSO)
    {
        this.mapSO = mapSO;
        this.GetPixelColorFp = Functions.GetPixelColorFp;
        this.GetPixelColor32Fp = Functions.GetPixelColor32Fp;
        this.GetPixelFloatFp = Functions.GetPixelFloatFp;
        this.GetPixelHeightAlphaFp = Functions.GetPixelHeightAlphaFp;
    }

    public static Guard Create(MapSO mapSO, out BurstMapSO burst)
    {
        MapSO* ptr = (MapSO*)UnsafeUtility.PinGCObjectAndGetAddress(mapSO, out var gcHandle);
        burst = new(ptr);
        return new() { gcHandle = gcHandle };
    }

    delegate void GetPixelColorDelegate(MapSO* gcHandle, float x, float y, out Color color);
    delegate void GetPixelColor32Delegate(MapSO* gcHandle, float x, float y, out Color32 color);
    delegate float GetPixelFloatDelegate(MapSO* gcHandle, float x, float y);
    delegate void GetPixelHeightAlphaDelegate(
        MapSO* gcHandle,
        float x,
        float y,
        out BurstMapSO.HeightAlpha value
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

    public readonly float GetPixelFloat(float x, float y) => GetPixelFloatFp.Invoke(mapSO, x, y);

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

    public readonly BurstMapSO.HeightAlpha GetPixelHeightAlpha(float x, float y)
    {
        GetPixelHeightAlphaFp.Invoke(mapSO, x, y, out var ha);
        return ha;
    }

    public readonly Color GetPixelColor(double x, double y) => GetPixelColor((float)x, (float)y);

    public readonly float GetPixelFloat(double x, double y) => GetPixelFloat((float)x, (float)y);

    public readonly Color32 GetPixelColor32(double x, double y) =>
        GetPixelColor32((float)x, (float)y);

    public readonly BurstMapSO.HeightAlpha GetPixelHeightAlpha(double x, double y) =>
        GetPixelHeightAlpha((float)x, (float)y);
}
