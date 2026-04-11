using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Jobs;

/// <summary>
/// Computes the min and max values of a float array.
/// </summary>
[BurstCompile]
internal struct TextureExportMinMaxJob : IJob
{
    [ReadOnly]
    public NativeArray<float> heights;

    /// <summary>[0] = min, [1] = max.</summary>
    public NativeArray<float> result;

    public void Execute()
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < heights.Length; i++)
        {
            float h = heights[i];
            min = math.min(min, h);
            max = math.max(max, h);
        }

        result[0] = min;
        result[1] = max;
    }
}

/// <summary>
/// Encodes heights to RGB24 greyscale (as Color32 with A=255), normalized to [0, 1]
/// between <see cref="minH"/> and <see cref="maxH"/>.
/// Matches the Parallax Continued export format.
/// </summary>
[BurstCompile]
internal struct TextureExportEncodeHeightsRGB24Job : IJobParallelForBatch
{
    [ReadOnly]
    public NativeArray<float> heights;

    [WriteOnly]
    public NativeArray<Color32> output;

    public float minH;
    public float maxH;

    public void Execute(int start, int count)
    {
        float range = maxH - minH;
        int end = start + count;

        if (range <= 0.0 || math.isnan(range) || math.isinf(range))
        {
            for (int i = start; i < end; ++i)
                output[i] = new(0, 0, 0, 255);
            return;
        }

        var invrange = math.rcp(range);

        for (int i = start; i < end; ++i)
        {
            byte v = (byte)(math.saturate((heights[i] - minH) * invrange) * 255f);
            output[i] = new(v, v, v, 255);
        }
    }
}

/// <summary>
/// Encodes heights to single-channel 16-bit (R16), normalized to [0, 1]
/// between <see cref="minH"/> and <see cref="maxH"/>.
/// </summary>
[BurstCompile]
internal struct TextureExportEncodeHeightsR16Job : IJobParallelForBatch
{
    [ReadOnly]
    public NativeArray<float> heights;

    [WriteOnly]
    public NativeArray<ushort> output;

    public float minH;
    public float maxH;

    public void Execute(int start, int count)
    {
        float range = maxH - minH;
        int end = start + count;

        if (range <= 0.0 || math.isnan(range) || math.isinf(range))
        {
            for (int i = start; i < end; ++i)
                output[i] = 0;
            return;
        }

        var invrange = math.rcp(range);

        for (int i = start; i < end; ++i)
            output[i] = (ushort)(math.saturate((heights[i] - minH) * invrange) * 65535f);
    }
}

/// <summary>
/// Flips a float texture vertically by swapping rows. Each batch element is a row index
/// in the top half; the job swaps it with the corresponding row from the bottom half.
/// </summary>
[BurstCompile]
internal struct TextureExportFlipFloatJob : IJobParallelForBatch
{
    [NativeDisableParallelForRestriction]
    public NativeArray<float> data;

    public int resX;
    public int resY;

    public void Execute(int start, int count)
    {
        int end = start + count;
        for (int y = start; y < end; y++)
        {
            int topOffset = y * resX;
            int bottomOffset = (resY - 1 - y) * resX;
            for (int x = 0; x < resX; x++)
            {
                var tmp = data[topOffset + x];
                data[topOffset + x] = data[bottomOffset + x];
                data[bottomOffset + x] = tmp;
            }
        }
    }
}

/// <summary>
/// Flips a Color32 texture vertically by swapping rows. Each batch element is a row index
/// in the top half; the job swaps it with the corresponding row from the bottom half.
/// </summary>
[BurstCompile]
internal struct TextureExportFlipColor32Job : IJobParallelForBatch
{
    [NativeDisableParallelForRestriction]
    public NativeArray<Color32> data;

    public int resX;
    public int resY;

    public void Execute(int start, int count)
    {
        int end = start + count;
        for (int y = start; y < end; y++)
        {
            int topOffset = y * resX;
            int bottomOffset = (resY - 1 - y) * resX;
            for (int x = 0; x < resX; x++)
            {
                var tmp = data[topOffset + x];
                data[topOffset + x] = data[bottomOffset + x];
                data[bottomOffset + x] = tmp;
            }
        }
    }
}
