using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Util;

public static class BurstUtil
{
    public static bool IsBurstCompiled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Managed(ref bool burst) => burst = false;

            bool burst = true;
            Managed(ref burst);
            return burst;
        }
    }

    internal static FunctionPointer<F> MaybeCompileFunctionPointer<F>(F del)
        where F : Delegate
    {
        try
        {
            return BurstCompiler.CompileFunctionPointer(del);
        }
        // If the delegate is not a valid burst-compiled function then we just get
        // a normal function pointer for it.
        catch (InvalidOperationException)
        {
            return new(Marshal.GetFunctionPointerForDelegate(del));
        }
    }

    public static unsafe T* Alloc<T>(T value, Allocator alloc = Allocator.Temp)
        where T : unmanaged
    {
        T* ptr = (T*)UnsafeUtility.Malloc(sizeof(T), UnsafeUtility.AlignOf<T>(), alloc);
        *ptr = value;
        return ptr;
    }

    // csharpier-ignore
    public static float4x4 ConvertMatrix(Matrix4x4 mat)
    {
        return new(
            mat.m00, mat.m01, mat.m02, mat.m03,
            mat.m10, mat.m11, mat.m12, mat.m13,
            mat.m20, mat.m21, mat.m22, mat.m23,
            mat.m30, mat.m31, mat.m32, mat.m33
        );
    }

    // csharpier-ignore
    public static double4x4 ConvertMatrix(Matrix4x4D mat)
    {
        return new(
            mat.m00, mat.m01, mat.m02, mat.m03,
            mat.m10, mat.m11, mat.m12, mat.m13,
            mat.m20, mat.m21, mat.m22, mat.m23,
            mat.m30, mat.m31, mat.m32, mat.m33
        );
    }

    public static double2 ConvertVector(Vector2d v) => new(v.x, v.y);

    public static double3 ConvertVector(Vector3d v) => new(v.x, v.y, v.z);

    public static double4 ConvertVector(Vector4d v) => new(v.x, v.y, v.z, v.w);

    public static Vector2d ConvertVector(double2 v) => new(v.x, v.y);

    public static Vector3d ConvertVector(double3 v) => new(v.x, v.y, v.z);

    public static Vector4d ConvertVector(double4 v) => new(v.x, v.y, v.z, v.w);

    public static float2 ConvertVector(Vector2 v) => new(v.x, v.y);

    public static float3 ConvertVector(Vector3 v) => new(v.x, v.y, v.z);

    public static float4 ConvertVector(Vector4 v) => new(v.x, v.y, v.z, v.w);

    public static Vector2 ConvertVector(float2 v) => new(v.x, v.y);

    public static Vector3 ConvertVector(float3 v) => new(v.x, v.y, v.z);

    public static Vector4 ConvertVector(float4 v) => new(v.x, v.y, v.z, v.w);

    public static Color ConvertColor(float4 c) => new(c.x, c.y, c.z, c.w);

    public static float4 ConvertColor(Color c) => new(c.r, c.g, c.b, c.a);
}
