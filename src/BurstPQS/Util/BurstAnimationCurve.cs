using System;
using System.Net;
using System.Runtime.CompilerServices;
using BurstPQS.Collections;
using BurstPQS.Patches;
using KSP.UI;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Burst.Intrinsics.X86;

namespace BurstPQS.Util;

/// <summary>
/// A burst-compatible equivalent to <see cref="AnimationCurve"/>.
/// </summary>
///
/// <remarks>
/// Be aware that every time you construct this type it needs to make a new
/// allocation. This is expensive so you should prefer to create it once and
/// keep it around rather than recreating it every time.
/// </remarks>
public struct BurstAnimationCurve : IDisposable
{
    /// <summary>
    /// Unity's Animation Curves cache the last sampled keyframe segment
    /// to avoid seeking the correct keyframe interval on each evaluation.
    /// This emulates that feature.
    /// </summary>
    internal struct CurveSampleCache
    {
        internal uint index;
    }

    public readonly struct Guard : IDisposable
    {
        readonly ulong gcHandle;

        internal Guard(ulong gcHandle) => this.gcHandle = gcHandle;

        public void Dispose()
        {
            UnsafeUtility.ReleaseGCObject(gcHandle);
        }
    }

    ulong gchandle;
    CurveSampleCache cache;
    ReadOnlyMemorySpan<Keyframe> keys;

    // Array of keyframe times, padded out to a multiple of 8 in length
    NativeArray<float> times;

    public unsafe BurstAnimationCurve(AnimationCurve curve)
    {
        if (curve is null)
            throw new ArgumentNullException(nameof(curve));
        if (curve.keys is null)
            throw new ArgumentException("curve had null keys");

        var keys = curve.keys;
        var data = UnsafeUtility.PinGCArrayAndGetDataAddress(curve.keys, out gchandle);
        this.keys = new((Keyframe*)data, keys.Length);
        this.times = new NativeArray<float>(RoundUpTo(keys.Length, 8), Allocator.Persistent);

        int i = 0;
        for (; i < keys.Length; ++i)
            times[i] = this.keys[i].time;

        int last = i - 1;
        for (; i < this.times.Length; ++i)
            times[i] = times[last];
    }

    public readonly void Dispose()
    {
        UnsafeUtility.ReleaseGCObject(gchandle);
        times.Dispose();
    }

    private struct DisposeJob(BurstAnimationCurve curve) : IJob
    {
        public readonly void Execute() => curve.Dispose();
    }

    public void Dispose(JobHandle job)
    {
        new DisposeJob(this).Schedule(job);
        this = default;
    }

    /// <summary>
    /// Returns the index of the first keyframe with time &gt;
    /// <paramref name="time"/>.
    /// </summary>
    /// <param name="time"></param>
    /// <returns></returns>
    private unsafe readonly int Search(float time)
    {
        if (Avx2.IsAvx2Supported)
        {
            var tptr = (float*)times.GetUnsafePtr();

            int i = 0;
            for (; i < times.Length; i += 8)
            {
                v256 word = Avx.mm256_loadu_ps(&tptr[i]);
                v256 cmp = Avx.mm256_cmp_ps(word, new v256(time), (int)Avx.CMP.GT_OQ);
                int mask = Avx.mm256_movemask_ps(cmp);

                if (mask == 0)
                    continue;

                return i + math.tzcnt(mask);
            }

            return keys.Length;
        }
        else
        {
            int i = 0;
            for (; i < times.Length; i += 4)
            {
                float4 word = new(times[i + 0], times[i + 1], times[i + 2], times[i + 3]);
                bool4 check = word > time;

                if (!math.any(check))
                    continue;

                // Once we have the mask in a word we can use tzcnt to directly
                // extract the first unset index.
                return i + math.tzcnt(math.bitmask(check));
            }

            return keys.Length;
        }
    }

    public unsafe float Evaluate(float time)
    {
        var nkeys = this.keys.Length;
        var keys = this.keys.GetUncheckedPointer();
        var times = (float*)this.times.GetUnsafePtr();

        if (Hint.Unlikely(nkeys == 0))
            return 0f;
        if (Hint.Unlikely(nkeys == 1))
            return keys[0].value;

        // Avoid checking out-of-bounds cache indices and just jump to the
        // search directly.
        if (Hint.Unlikely(cache.index + 1 >= (uint)this.keys.Length))
            goto DoSearch;

        // This need to be initialized to start so we point them to a dummy variable
        var dummy = default(Keyframe);
        ref readonly Keyframe k0 = ref dummy;
        ref readonly Keyframe k1 = ref dummy;

        // Test the cache first.
        var vtimes = *(float2*)&times[cache.index];
        if (vtimes.x < time && time <= vtimes.y)
        {
            k0 = ref keys[cache.index];
            // this lets the compiler figure out it can just load from k0 + 28
            k1 = ref Unsafe.Add(ref Unsafe.AsRef(in k0), 1);

            goto Eval;
        }

        DoSearch:
        int index = Search(time);
        if (index == 0)
            return keys[0].value;
        if (index >= nkeys)
            return keys[nkeys - 1].value;

        cache.index = (uint)index - 1;

        k0 = ref keys[index - 1];
        k1 = ref Unsafe.Add(ref Unsafe.AsRef(in k0), 1);

        Eval:
        return Interpolate(time, in k0, in k1);
    }

    // Burst inlines static readonly fields.
    // csharpier-ignore
    static readonly float4x4 matrix = new(
         2f, -3f, 0f, 1f,
         1f, -2f, 1f, 0f,
        -2f,  3f, 0f, 0f,
         1f, -1f, 0f, 0f
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Interpolate(float time, in Keyframe k0, in Keyframe k1)
    {
        float dt = k1.time - k0.time;
        float4 u;
        u.w = 1f;
        u.z = (time - k0.time) * math.rcp(dt);
        u.y = u.z * u.z; // u squared
        u.x = u.y * u.z; // u squared times u give u cubed

        float2 m = new float2(k0.outTangent, k1.inTangent) * dt;

        // Burst should compile this as four vec-by-float muls and some float4 additions.
        float4 weights = math.mul(matrix, u);
        // Componentwise mul and then summation is just a dot product.
        // So let's define it as such and let Burst worry about optimizing it.
        return math.dot(weights, new float4(k0.value, m.x, k1.value, m.y));
    }

    static int RoundUpTo(int v, int mult) => (v + (mult - 1)) / mult * mult;
}
