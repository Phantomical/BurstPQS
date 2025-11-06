using System;
using BurstPQS.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstPQS.Util;

public struct BurstAnimationCurve
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

    public static unsafe Guard Create(AnimationCurve curve, out BurstAnimationCurve burst)
    {
        var keys = curve.keys;
        var data = UnsafeUtility.PinGCArrayAndGetDataAddress(keys, out var gcHandle);

        burst = new() { keys = new((Keyframe*)data, keys.Length) };

        return new(gcHandle);
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

            if (k1.time > time)
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
