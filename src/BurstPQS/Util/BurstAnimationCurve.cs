using System;
using BurstPQS.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Util;

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

    public readonly float Evaluate(float time)
    {
        if (keys.Length == 0)
            return 0f;

        // TODO: This isn't technically correct, since animations can have lots of
        //       different wrap modes. KSP doens't seem to use them though.
        if (time < keys[0].time)
            return keys[0].value;

        for (int i = 1; i < keys.Length; ++i)
        {
            ref readonly Keyframe k0 = ref keys[i - 1];
            ref readonly Keyframe k1 = ref keys[i];

            if (time > k1.time)
                continue;

            return Interpolate(time, in k0, in k1);
        }

        return keys[keys.Length - 1].value;
    }

    static float Interpolate(float time, in Keyframe k0, in Keyframe k1)
    {
        float dt = k1.time - k0.time;
        float t = (time - k0.time) / dt;

        float m0 = k0.outTangent * dt;
        float m1 = k1.inTangent * dt;

        float t2 = t * t;
        float t3 = t * t2;

        float a = 2 * t3 - 3 * t2 + 1;
        float b = t3 - 2 * t2 + t;
        float c = t3 - t2;
        float d = -2 * t3 + 3 * t2;

        return a * k0.value + b * m0 + c * m1 + d * k1.value;
    }
}
