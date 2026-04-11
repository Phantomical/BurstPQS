using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Jobs;

[BurstCompile]
internal struct TextureExportClampJob : IJobParallelForBatch
{
    public NativeArray<float> values;
    public float min;
    public float max;

    public void Execute(int start, int count)
    {
        int end = start + count;

        for (int i = start; i < end; ++i)
            values[i] = Mathf.Clamp(values[i], min, max);
    }
}
