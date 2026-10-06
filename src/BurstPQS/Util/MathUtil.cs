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

    // Loads 4 float3s and transposes them into one float4 per component.
    public static unsafe void LoadTransposed(float3* p, out float4 x, out float4 y, out float4 z)
    {
        if (Avx2.IsAvx2Supported)
        {
            // lo = (x0 y0 z0 x1 | y1 z1 x2 y2), hi = (z2 x3 y3 z3 | z2 x3 y3 z3)
            v256 lo = Avx.mm256_loadu_ps(p);
            v256 hi = Avx.mm256_broadcast_ps((float*)p + 8);

            // Blend the last 4 floats into slots that aren't needed, then
            // permute everything into place.
            v256 xy = Avx.mm256_blend_ps(lo, hi, 0b0010_0100); // x0 y0 y3 x1 | y1 x3 x2 y2
            v256 zs = Avx.mm256_blend_ps(lo, hi, 0b1000_0001); // z2 y0 z0 x1 | y1 z1 x2 z3

            xy = Avx2.mm256_permutevar8x32_ps(xy, new v256(0, 3, 6, 5, 1, 4, 7, 2));
            zs = Avx2.mm256_permutevar8x32_ps(zs, new v256(2, 5, 0, 7, 2, 5, 0, 7));

            v128 xs = Avx.mm256_castps256_ps128(xy);
            v128 ys = Avx.mm256_extractf128_ps(xy, 1);
            v128 zl = Avx.mm256_castps256_ps128(zs);

            x = *(float4*)&xs;
            y = *(float4*)&ys;
            z = *(float4*)&zl;
        }
        else
        {
            x = new float4(p[0].x, p[1].x, p[2].x, p[3].x);
            y = new float4(p[0].y, p[1].y, p[2].y, p[3].y);
            z = new float4(p[0].z, p[1].z, p[2].z, p[3].z);
        }
    }

    // Transposes one float4 per component back into 4 float4s and stores them.
    public static unsafe void StoreTransposed(float4* p, float4 x, float4 y, float4 z, float4 w)
    {
        if (Avx2.IsAvx2Supported)
        {
            v256 xz = Avx.mm256_insertf128_ps(Avx.mm256_castps128_ps256(*(v128*)&x), *(v128*)&z, 1);
            v256 yw = Avx.mm256_insertf128_ps(Avx.mm256_castps128_ps256(*(v128*)&y), *(v128*)&w, 1);

            v256 lo = Avx2.mm256_unpacklo_epi32(xz, yw); // x0 y0 x1 y1 | z0 w0 z1 w1
            v256 hi = Avx2.mm256_unpackhi_epi32(xz, yw); // x2 y2 x3 y3 | z2 w2 z3 w3

            Avx.mm256_storeu_ps(p + 0, Avx2.mm256_permute4x64_pd(lo, 0b11_01_10_00));
            Avx.mm256_storeu_ps(p + 2, Avx2.mm256_permute4x64_pd(hi, 0b11_01_10_00));
        }
        else
        {
            p[0] = new float4(x.x, y.x, z.x, w.x);
            p[1] = new float4(x.y, y.y, z.y, w.y);
            p[2] = new float4(x.z, y.z, z.z, w.z);
            p[3] = new float4(x.w, y.w, z.w, w.w);
        }
    }

    internal static float8 RsqrtApprox(float8 x)
    {
        if (!Avx.IsAvxSupported)
            return new(RsqrtApprox(x.lo), RsqrtApprox(x.hi));

        float8 r = Avx.mm256_rsqrt_ps(x);
        return r * (1.5f - 0.5f * x * r * r);
    }

    // Same as math.select: c ? b : a.
    internal static float8 Select(float8 a, float8 b, bool8 c)
    {
        if (Avx.IsAvxSupported)
            return Avx.mm256_blendv_ps(a, b, c);
        return new(math.select(a.lo, b.lo, c.lo != 0), math.select(a.hi, b.hi, c.hi != 0));
    }

    // Loads 8 float3s and transposes them into one float8 per component.
    //
    // With AVX2 the lanes hold vertices 0 2 4 6 1 3 5 7, which is the order
    // StoreTransposed8 can write back without crossing 128-bit lanes. Only
    // use the result for lane-wise math and pair it with StoreTransposed8.
    internal static unsafe void LoadTransposed8(
        float3* p,
        out float8 x,
        out float8 y,
        out float8 z
    )
    {
        if (Avx2.IsAvx2Supported)
        {
            float* f = (float*)p;

            // a = (x0 y0 z0 x1 y1 z1 x2 y2)
            // b = (z2 x3 y3 z3 x4 y4 z4 x5)
            // c = (y5 z5 x6 y6 z6 x7 y7 z7)
            v256 a = Avx.mm256_loadu_ps(f);
            v256 b = Avx.mm256_loadu_ps(f + 8);
            v256 c = Avx.mm256_loadu_ps(f + 16);

            // Each component sits in a different set of slots in a, b and c,
            // so two blends gather it into one register and a permute sorts it.
            v256 xs = Avx.mm256_blend_ps(Avx.mm256_blend_ps(a, b, 0b1001_0010), c, 0b0010_0100); // x0 x3 x6 x1 x4 x7 x2 x5
            v256 ys = Avx.mm256_blend_ps(Avx.mm256_blend_ps(a, b, 0b0010_0100), c, 0b0100_1001); // y5 y0 y3 y6 y1 y4 y7 y2
            v256 zs = Avx.mm256_blend_ps(Avx.mm256_blend_ps(a, b, 0b0100_1001), c, 0b1001_0010); // z2 z5 z0 z3 z6 z1 z4 z7

            x = Avx2.mm256_permutevar8x32_ps(xs, new v256(0, 6, 4, 2, 3, 1, 7, 5));
            y = Avx2.mm256_permutevar8x32_ps(ys, new v256(1, 7, 5, 3, 4, 2, 0, 6));
            z = Avx2.mm256_permutevar8x32_ps(zs, new v256(2, 0, 6, 4, 5, 3, 1, 7));
        }
        else
        {
            LoadTransposed(p + 0, out float4 xl, out float4 yl, out float4 zl);
            LoadTransposed(p + 4, out float4 xh, out float4 yh, out float4 zh);
            x = new(xl, xh);
            y = new(yl, yh);
            z = new(zl, zh);
        }
    }

    // Transposes one float8 per component back into 8 float4s and stores them.
    // Expects the lane order produced by LoadTransposed8.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void StoreTransposed8(
        float4* p,
        float8 x,
        float8 y,
        float8 z,
        float8 w
    )
    {
        if (Avx2.IsAvx2Supported)
        {
            // Burst splits mm256_unpacklo_ps into two 128-bit unpacks, but the
            // epi32 version compiles to a single 256-bit vunpcklps.
            v256 xy0 = Avx2.mm256_unpacklo_epi32(x, y); // x0 y0 x2 y2 | x1 y1 x3 y3
            v256 xy1 = Avx2.mm256_unpackhi_epi32(x, y); // x4 y4 x6 y6 | x5 y5 x7 y7
            v256 zw0 = Avx2.mm256_unpacklo_epi32(z, w);
            v256 zw1 = Avx2.mm256_unpackhi_epi32(z, w);

            Avx.mm256_storeu_ps(p + 0, Avx.mm256_unpacklo_pd(xy0, zw0)); // v0 | v1
            Avx.mm256_storeu_ps(p + 2, Avx.mm256_unpackhi_pd(xy0, zw0)); // v2 | v3
            Avx.mm256_storeu_ps(p + 4, Avx.mm256_unpacklo_pd(xy1, zw1)); // v4 | v5
            Avx.mm256_storeu_ps(p + 6, Avx.mm256_unpackhi_pd(xy1, zw1)); // v6 | v7
        }
        else
        {
            StoreTransposed(p + 0, x.lo, y.lo, z.lo, w.lo);
            StoreTransposed(p + 4, x.hi, y.hi, z.hi, w.hi);
        }
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
