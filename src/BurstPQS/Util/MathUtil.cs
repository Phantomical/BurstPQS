using System;
using System.Runtime.CompilerServices;
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

    public static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

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
}
