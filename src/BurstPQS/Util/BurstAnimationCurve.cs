using System;
using BurstPQS.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace BurstPQS.Util;

/// <summary>
/// Unity's Animation Curves cache the last sampled keyframe segment
/// to avoid seeking the correct keyframe interval on each evaluation.
/// This emulates that feature.
/// </summary>
public struct CurveSampleCache
{
    internal int index;
    internal float time;
}

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
    public readonly struct Guard : IDisposable
    {
        readonly ulong gcHandle;

        internal Guard(ulong gcHandle) => this.gcHandle = gcHandle;

        public void Dispose()
        {
            UnsafeUtility.ReleaseGCObject(gcHandle);
        }
    }

    ReadOnlyMemorySpan<Keyframe> keys;
    ulong gchandle;

    public unsafe BurstAnimationCurve(AnimationCurve curve)
    {
        if (curve is null)
            throw new ArgumentNullException(nameof(curve));
        if (curve.keys is null)
            throw new ArgumentException("curve had null keys");

        var keys = curve.keys;
        var data = UnsafeUtility.PinGCArrayAndGetDataAddress(curve.keys, out gchandle);
        this.keys = new((Keyframe*)data, keys.Length);
    }

    public readonly void Dispose()
    {
        UnsafeUtility.ReleaseGCObject(gchandle);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int BinarySearch(float time)
    {
        // Binary search finding the rightmost element.
        int2 bounds = new int2(0, keys.Length);
        while (bounds.x < bounds.y)
        {
            // SRL by x = floored integer div by 2^x.
            int M = bounds.x + ((bounds.y - bounds.x) >> 1);
            ref readonly Keyframe test = ref keys[M];
            if (test.time > time)
                bounds.y = M;
            else
                bounds.x = M + 1;
        }
        // Technically this should return bounds.y - 1.
        // That would yield the index of the left keyframe, or k0.
        // But that's unnecessary math here.
        // Because we'd want to add 1 back to get the right keyframe (k1) of the interval.
        // Best do the subtraction in the caller so we have 1 sub instead of 1 sub 1 add.
        return bounds.y;
    }

    public readonly float Evaluate(float time)
    {
        switch (keys.Length)
        {
            case 0:
                return 0f;
            case 1:
                return keys[0].value;
            default:
                // TODO: This isn't technically correct, since animations can have lots of
                //       different wrap modes. KSP doesn't seem to use them though.
                ref readonly Keyframe test = ref keys[0];
                if (time < test.time)
                    return test.value;

                // Test the other bound too.
                test = ref keys[keys.Length - 1];
                if (time > test.time)
                    return test.value;

                int segment = BinarySearch(time);
                return Interpolate(time, in keys[segment - 1], in keys[segment]);
        }
    }

    public readonly float EvaluateCached(float time, ref CurveSampleCache cache)
    {
        switch (keys.Length)
        {
            case 0:
                return 0f;
            case 1:
                return keys[0].value;
            default:
                // Test the cache first.
                int nb = cache.index + 1;
                ref readonly Keyframe k0 = ref keys[cache.index];
                ref readonly Keyframe k1 = ref keys[nb];
                if (k0.time < time && time <= k1.time)
                {
                    // Cache hit!
                    cache.time = time;
                    goto Eval; // Skip ahead. k0 and k1 are fine.
                }

                // Cache missed. Try a neighbouring segment.
                // We will need to check which one, however.
                // if time < cache.time then we should seek to the left.
                // And vice versa, to the right.
                // time =/= cache.time or the cache would have hit.
                // That, or someone donkey'd with the cache and the time and keyframe are misaligned.
                // In that case we'll automatically end up in the binsearch to rectify that.
                if (time < cache.time)
                {
                    // Test the segment to the left.
                    // Ensure such a segment actually exists.
                    if (cache.index > 0)
                    {
                        nb = cache.index - 1;
                        // k1 becomes keys[cache.index] ergo what k0 was.
                        k1 = ref k0;
                        k0 = ref keys[nb];
                        if (k0.time < time && time < k1.time)
                        {
                            // Success!
                            cache.time = time;
                            cache.index = nb;
                            goto Eval; // Skip the binsearch.
                        }
                    }
                }
                else
                {
                    // Test the segment to the right.
                    // Again test if that's actually real.
                    int nb2 = nb + 1;
                    if (nb2 < keys.Length)
                    {
                        // k0 becomes keys[nb] ergo what k1 was.
                        k0 = ref k1;
                        k1 = ref keys[nb2];
                        if (k0.time < time && time <= k1.time)
                        {
                            // Success!
                            cache.time = time;
                            cache.index = nb;
                            goto Eval;
                        }
                    }
                }

                // No luck. Fallback to a boundary test and a binsearch.
            
                // TODO: This isn't technically correct, since animations can have lots of
                //       different wrap modes. KSP doesn't seem to use them though.
                ref readonly Keyframe test = ref keys[0];
                if (time < test.time)
                    return test.value;

                // Test the other bound too.
                test = ref keys[keys.Length - 1];
                if (time > test.time)
                    return test.value;

                int segment = BinarySearch(time);
                cache.time = time;

                // Remember that the binsearch yields the index of k1.
                // For k0 we sub 1.
                // We also want that value captured so let's reuse nb.
                nb = segment - 1;
                cache.index = nb;
                k0 = ref keys[nb];
                k1 = ref keys[segment];
                
            Eval:; // Jump label in case we can skip the seek.
                return Interpolate(time, in k0, in k1);
        }
    }

    // Burst inlines static readonly fields.
    static readonly float4x4 matrix = new float4x4(
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
}
