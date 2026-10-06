using System;
using System.Runtime.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Burst.Intrinsics.X86;
using static Unity.Burst.Intrinsics.X86.Bmi1;
using static Unity.Burst.Intrinsics.X86.Popcnt;

namespace BurstPQS.Util;

public static class MathUtil
{
    public static double CubicHermite(
        double start,
        double end,
        double startTangent,
        double endTangent,
        double t
    )
    {
        double ct2 = t * t;
        double ct3 = ct2 * t;
        return start * (2.0 * ct3 - 3.0 * ct2 + 1.0)
            + startTangent * (ct3 - 2.0 * ct2 + t)
            + end * (-2.0 * ct3 + 3.0 * ct2)
            + endTangent * (ct3 - ct2);
    }

    public static double Lerp(double v2, double v1, double dt)
    {
        return v1 * dt + v2 * (1.0 - dt);
    }

    public static double Clamp(double v, double min, double max) => Math.Min(Math.Max(v, min), max);

    public static int Clamp(int v, int min, int max) => Math.Min(Math.Max(v, min), max);

    public static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

    // Matches Unity's native Vector3.OrthoNormalize, which Burst can only
    // reach through an icall.
    public static void OrthoNormalize(ref Vector3 normal, ref Vector3 tangent)
    {
        float3 u = normal;
        float3 v = tangent;

        float mag = math.sqrt(math.dot(u, u));
        if (mag > Vector3.kEpsilon)
            u /= mag;
        else
            u = new float3(1f, 0f, 0f);

        v -= math.dot(u, v) * u;
        mag = math.sqrt(math.dot(v, v));
        if (mag < Vector3.kEpsilon)
            v = OrthoNormalVector(u);
        else
            v /= mag;

        normal = u;
        tangent = v;
    }

    // rsqrtps plus one Newton step, about 22 bits of precision. Keeps the
    // divider free, unlike sqrt + divide.
    public static unsafe float4 RsqrtApprox(float4 x)
    {
        if (!Sse.IsSseSupported)
            return math.rsqrt(x);

        v128 est = Sse.rsqrt_ps(*(v128*)&x);
        float4 r = *(float4*)&est;
        return r * (1.5f - 0.5f * x * r * r);
    }

    static float3 OrthoNormalVector(float3 n)
    {
        const float OneOverSqrt2 = 0.7071067811865475244008443621048490f;

        if (math.abs(n.z) > OneOverSqrt2)
        {
            float k = 1f / math.sqrt(n.y * n.y + n.z * n.z);
            return new float3(0f, -n.z * k, n.y * k);
        }
        else
        {
            float k = 1f / math.sqrt(n.x * n.x + n.y * n.y);
            return new float3(-n.y * k, n.x * k, 0f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int PopCount(ulong x)
    {
        if (IsPopcntSupported)
            return popcnt_u64(x);

        x -= (x >> 1) & 0x5555555555555555;
        x = (x & 0x3333333333333333) + ((x >> 2) & 0x3333333333333333);
        x = (x + (x >> 4)) & 0xF0F0F0F0F0F0F0F;
        return (int)((x * 0x101010101010101) >> 56);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TrailingZeroCount(ulong v)
    {
        if (IsBmi1Supported)
            return (int)tzcnt_u64(v);

        int c = 64;

        v &= (ulong)-(long)v;
        if (v != 0)
            c--;
        if ((v & 0x00000000FFFFFFFF) != 0)
            c -= 32;
        if ((v & 0x0000FFFF0000FFFF) != 0)
            c -= 16;
        if ((v & 0x00FF00FF00FF00FF) != 0)
            c -= 8;
        if ((v & 0x0F0F0F0F0F0F0F0F) != 0)
            c -= 4;
        if ((v & 0x3333333333333333) != 0)
            c -= 2;
        if ((v & 0x5555555555555555) != 0)
            c -= 1;

        return c;
    }

    /// <summary>
    /// An equivalent to <see cref="Vector3d.normalized"/> that can be used in
    /// burst-compiled code. KSP's obfuscation makes it so that burst is unable
    /// to compile <see cref="Vector3d.Normalize(Vector3d)"/>, so you will need
    /// to use this instead.
    /// </summary>
    /// <param name="v"></param>
    /// <returns></returns>
    public static Vector3d Normalized(this Vector3d v) =>
        BurstUtil.ConvertVector(math.normalize(BurstUtil.ConvertVector(v)));
}
