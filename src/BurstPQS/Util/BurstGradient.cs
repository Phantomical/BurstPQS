using System;
using BurstPQS.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Util;

public struct BurstGradient : IDisposable
{
    GradientMode mode;
    MemorySpan<GradientColorKey> colorKeys;
    MemorySpan<GradientAlphaKey> alphaKeys;
    ulong colorGcHandle;
    ulong alphaGcHandle;

    public unsafe BurstGradient(Gradient gradient)
    {
        mode = gradient.mode;
        var colorKeys = UnsafeUtility.PinGCArrayAndGetDataAddress(
            gradient.colorKeys,
            out colorGcHandle
        );
        var alphaKeys = UnsafeUtility.PinGCArrayAndGetDataAddress(
            gradient.alphaKeys,
            out alphaGcHandle
        );

        this.colorKeys = new((GradientColorKey*)colorKeys, gradient.colorKeys.Length);
        this.alphaKeys = new((GradientAlphaKey*)alphaKeys, gradient.alphaKeys.Length);
    }

    public readonly void Dispose()
    {
        UnsafeUtility.ReleaseGCObject(colorGcHandle);
        UnsafeUtility.ReleaseGCObject(alphaGcHandle);
    }

    struct DisposeJob(BurstGradient gradient) : IJob
    {
        public readonly void Execute() => gradient.Dispose();
    }

    public void Dispose(JobHandle handle)
    {
        new DisposeJob(this).Schedule(handle);
        this = default;
    }

    public readonly Color Evaluate(float time)
    {
        return mode switch
        {
            GradientMode.Blend => EvaluateBlend(time),
            GradientMode.Fixed => EvaluateFixed(time),
            _ => Color.magenta,
        };
    }

    readonly Color EvaluateBlend(float time)
    {
        if (time <= colorKeys[0].time)
            return colorKeys[0].color;

        for (int i = 1; i < colorKeys.Length; ++i)
        {
            if (time > colorKeys[i].time)
                continue;

            var dt = colorKeys[i].time - colorKeys[i - 1].time;
            var c0 = colorKeys[i - 1].color;
            var c1 = colorKeys[i].color;

            return Color.Lerp(c0, c1, (time - colorKeys[i - 1].time) / dt);
        }

        return colorKeys[colorKeys.Length - 1].color;
    }

    readonly float EvaluateAlphaBlend(float time)
    {
        if (time <= alphaKeys[0].time)
            return alphaKeys[0].alpha;

        for (int i = 1; i < colorKeys.Length; ++i)
        {
            if (time > alphaKeys[i].time)
                continue;

            var dt = alphaKeys[i].time - alphaKeys[i - 1].time;
            var c0 = alphaKeys[i - 1].alpha;
            var c1 = alphaKeys[i].alpha;

            return Mathf.Lerp(c0, c1, (time - colorKeys[i - 1].time) / dt);
        }

        return alphaKeys[alphaKeys.Length - 1].alpha;
    }

    readonly Color EvaluateFixed(float time)
    {
        if (time < colorKeys[0].time)
            return colorKeys[0].color;

        for (int i = 1; i < colorKeys.Length; ++i)
        {
            if (colorKeys[i - 1].time < time)
                return colorKeys[i].color;
        }

        return colorKeys[colorKeys.Length - 1].color;
    }

    readonly float EvaluateAlphaFixed(float time)
    {
        if (time < alphaKeys[0].time)
            return alphaKeys[0].alpha;

        for (int i = 1; i < alphaKeys.Length; ++i)
        {
            if (alphaKeys[i - 1].time < time)
                return alphaKeys[i].alpha;
        }

        return alphaKeys[alphaKeys.Length - 1].alpha;
    }
}
