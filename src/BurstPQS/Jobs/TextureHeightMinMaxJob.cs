using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BurstPQS.Jobs;

[BurstCompile]
internal struct TextureHeightMinMaxInitJob : IJob
{
    public NativeArray<float> min;
    public NativeArray<float> max;

    public void Execute()
    {
        for (int i = 0; i < min.Length; ++i)
            min[i] = float.PositiveInfinity;

        for (int i = 0; i < max.Length; ++i)
            max[i] = float.NegativeInfinity;
    }
}

[BurstCompile]
internal struct TextureHeightMinMaxWideJob : IJobParallelForBatch
{
    public NativeArray<float> min;
    public NativeArray<float> max;

    public NativeArray<float> heights;

    [NativeSetThreadIndex]
    public int thread;

    public void Execute(int start, int count)
    {
        int end = start + count;

        float4 vmin = new(float.PositiveInfinity);
        float4 vmax = new(float.NegativeInfinity);

        int i = start;
        for (; i + 4 < end; i += 4)
        {
            float4 value = new(heights[i], heights[i + 1], heights[i + 2], heights[i + 3]);

            vmin = math.min(vmin, value);
            vmax = math.max(vmax, value);
        }

        float min = math.min(math.min(vmin.x, vmin.y), math.min(vmin.z, vmin.w));
        float max = math.max(math.max(vmax.x, vmax.y), math.max(vmax.z, vmax.w));

        for (; i < end; i++)
        {
            min = math.min(min, heights[i]);
            max = math.max(max, heights[i]);
        }

        this.min[thread] = math.min(min, this.min[thread]);
        this.max[thread] = math.max(max, this.max[thread]);
    }
}

[BurstCompile]
internal struct TextureHeightMinMaxNarrowJob : IJob
{
    [DeallocateOnJobCompletion]
    public NativeArray<float> min;

    [DeallocateOnJobCompletion]
    public NativeArray<float> max;
    public NativeArray<float> minmax;

    public void Execute()
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
            min = math.min(min, this.min[i]);

        for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
            max = math.max(max, this.max[i]);

        minmax[0] = min;
        minmax[1] = max;
    }

    public static JobHandle Schedule(
        NativeArray<float> heights,
        NativeArray<float> minmax,
        JobHandle dependsOn = default
    )
    {
        NativeArray<float> min = new(JobsUtility.MaxJobThreadCount, Allocator.TempJob);
        NativeArray<float> max = new(JobsUtility.MaxJobThreadCount, Allocator.TempJob);

        var handle = new TextureHeightMinMaxInitJob { min = min, max = max }.Schedule(dependsOn);
        handle = new TextureHeightMinMaxWideJob
        {
            min = min,
            max = max,
            heights = heights,
        }.ScheduleBatch(heights.Length, 4096, handle);
        return new TextureHeightMinMaxNarrowJob
        {
            min = min,
            max = max,
            minmax = minmax,
        }.Schedule(handle);
    }
}
