using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Map.Detail;

public interface IMapSO_GetHeight
{
    public int Height { get; }
}

public interface IMapSO_GetWidth
{
    public int Width { get; }
}

[JobProducerType(typeof(MapSOExtensions.GetPixelFloat_Int<>))]
public interface IMapSO_GetPixelFloat_Int
{
    float GetPixelFloat(int x, int y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelFloat_Float<>))]
public interface IMapSO_GetPixelFloat_Float
{
    float GetPixelFloat(float x, float y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelFloat_Double<>))]
public interface IMapSO_GetPixelFloat_Double
{
    float GetPixelFloat(double x, double y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelColor_Int<>))]
public interface IMapSO_GetPixelColor_Int
{
    Color GetPixelColor(int x, int y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelColor_Float<>))]
public interface IMapSO_GetPixelColor_Float
{
    Color GetPixelColor(float x, float y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelColor_Double<>))]
public interface IMapSO_GetPixelColor_Double
{
    Color GetPixelColor(double x, double y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelColor32_Int<>))]
public interface IMapSO_GetPixelColor32_Int
{
    Color32 GetPixelColor32(int x, int y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelColor32_Float<>))]
public interface IMapSO_GetPixelColor32_Float
{
    Color32 GetPixelColor32(float x, float y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelColor32_Double<>))]
public interface IMapSO_GetPixelColor32_Double
{
    Color32 GetPixelColor32(double x, double y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelHeightAlpha_Int<>))]
public interface IMapSO_GetPixelHeightAlpha_Int
{
    HeightAlpha GetPixelHeightAlpha(int x, int y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelHeightAlpha_Float<>))]
public interface IMapSO_GetPixelHeightAlpha_Float
{
    HeightAlpha GetPixelHeightAlpha(float x, float y);
}

[JobProducerType(typeof(MapSOExtensions.GetPixelHeightAlpha_Double<>))]
public interface IMapSO_GetPixelHeightAlpha_Double
{
    HeightAlpha GetPixelHeightAlpha(double x, double y);
}

internal unsafe struct MapSOExtensions
{
    public static void Dispose<TD>(void* self)
        where TD : struct, IDisposable
    {
        Unsafe.AsRef<TD>(self).Dispose();
    }

    internal struct GetPixelFloat_Int<T>
        where T : IMapSO_GetPixelFloat_Int
    {
        [BurstCompile]
        public static float Execute(void* self, int x, int y) =>
            Unsafe.AsRef<T>(self).GetPixelFloat(x, y);
    }

    internal struct GetPixelFloat_Float<T>
        where T : IMapSO_GetPixelFloat_Float
    {
        [BurstCompile]
        public static float Execute(void* self, float x, float y) =>
            Unsafe.AsRef<T>(self).GetPixelFloat(x, y);
    }

    internal struct GetPixelFloat_Double<T>
        where T : IMapSO_GetPixelFloat_Double
    {
        [BurstCompile]
        public static float Execute(void* self, double x, double y) =>
            Unsafe.AsRef<T>(self).GetPixelFloat(x, y);
    }

    internal struct GetPixelColor_Int<T>
        where T : IMapSO_GetPixelColor_Int
    {
        [BurstCompile]
        public static void Execute(void* self, int x, int y, out Color color) =>
            color = Unsafe.AsRef<T>(self).GetPixelColor(x, y);
    }

    internal struct GetPixelColor_Float<T>
        where T : IMapSO_GetPixelColor_Float
    {
        [BurstCompile]
        public static void Execute(void* self, float x, float y, out Color color) =>
            color = Unsafe.AsRef<T>(self).GetPixelColor(x, y);
    }

    internal struct GetPixelColor_Double<T>
        where T : IMapSO_GetPixelColor_Double
    {
        [BurstCompile]
        public static void Execute(void* self, double x, double y, out Color color) =>
            color = Unsafe.AsRef<T>(self).GetPixelColor(x, y);
    }

    internal struct GetPixelColor32_Int<T>
        where T : IMapSO_GetPixelColor32_Int
    {
        [BurstCompile]
        public static void Execute(void* self, int x, int y, out Color32 color) =>
            color = Unsafe.AsRef<T>(self).GetPixelColor32(x, y);
    }

    internal struct GetPixelColor32_Float<T>
        where T : IMapSO_GetPixelColor32_Float
    {
        [BurstCompile]
        public static void Execute(void* self, float x, float y, out Color32 color) =>
            color = Unsafe.AsRef<T>(self).GetPixelColor32(x, y);
    }

    internal struct GetPixelColor32_Double<T>
        where T : IMapSO_GetPixelColor32_Double
    {
        [BurstCompile]
        public static void Execute(void* self, double x, double y, out Color32 color) =>
            color = Unsafe.AsRef<T>(self).GetPixelColor32(x, y);
    }

    internal struct GetPixelHeightAlpha_Int<T>
        where T : IMapSO_GetPixelHeightAlpha_Int
    {
        [BurstCompile]
        public static void Execute(void* self, int x, int y, out HeightAlpha color) =>
            color = Unsafe.AsRef<T>(self).GetPixelHeightAlpha(x, y);
    }

    internal struct GetPixelHeightAlpha_Float<T>
        where T : IMapSO_GetPixelHeightAlpha_Float
    {
        [BurstCompile]
        public static void Execute(void* self, float x, float y, out HeightAlpha color) =>
            color = Unsafe.AsRef<T>(self).GetPixelHeightAlpha(x, y);
    }

    internal struct GetPixelHeightAlpha_Double<T>
        where T : IMapSO_GetPixelHeightAlpha_Double
    {
        [BurstCompile]
        public static void Execute(void* self, double x, double y, out HeightAlpha color) =>
            color = Unsafe.AsRef<T>(self).GetPixelHeightAlpha(x, y);
    }
}
