using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace BurstPQS.Jobs;

enum SubdivisionAction : byte
{
    None = 0,
    Subdivide = 1,
    Collapse = 2,
}

/// <summary>
/// Burst-compiled job that scans the actions array and collects indices
/// matching a specific action into a NativeList.
/// </summary>
[BurstCompile]
struct CollectActionsJob : IJob
{
    [ReadOnly]
    public NativeArray<SubdivisionAction> actions;
    public SubdivisionAction target;
    public NativeList<int> indices;

    public void Execute()
    {
        for (int i = 0; i < actions.Length; i++)
            if (actions[i] == target)
                indices.Add(i);
    }
}
