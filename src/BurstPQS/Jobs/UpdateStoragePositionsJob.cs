using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace BurstPQS.Jobs;

[BurstCompile]
struct UpdateStoragePositionsJob : IJobParallelForTransform
{
    public double4x4 planetToWorld;

    [ReadOnly]
    public NativeArray<double3> planetPositions;

    public void Execute(int index, TransformAccess transform)
    {
        var position = math.mul(planetToWorld, new double4(planetPositions[index], 1.0)).xyz;
        transform.position = new Vector3((float)position.x, (float)position.y, (float)position.z);
    }
}
