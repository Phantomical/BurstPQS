using System.Collections.Generic;
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

// The active quad list has no tree order. Subdivide parents before children,
// nearest first; collapse children before parents, farthest first.
[BurstCompile]
struct CollectActionsJob : IJob
{
    [ReadOnly]
    public NativeArray<SubdivisionAction> actions;

    [ReadOnly]
    public NativeArray<QuadSnapshot> snapshots;

    [ReadOnly]
    public NativeArray<QuadResult> results;
    public SubdivisionAction target;
    public NativeList<int> indices;

    public void Execute()
    {
        for (int i = 0; i < actions.Length; i++)
            if (actions[i] == target)
                indices.Add(i);

        indices.Sort(
            new ActionOrder
            {
                snapshots = snapshots,
                results = results,
                deepestFirst = target == SubdivisionAction.Collapse,
            }
        );
    }

    struct ActionOrder : IComparer<int>
    {
        public NativeArray<QuadSnapshot> snapshots;
        public NativeArray<QuadResult> results;
        public bool deepestFirst;

        public int Compare(int a, int b)
        {
            if (deepestFirst)
                (a, b) = (b, a);

            int cmp = snapshots[a].subdivision.CompareTo(snapshots[b].subdivision);
            if (cmp != 0)
                return cmp;

            return results[a].gcDist.CompareTo(results[b].gcDist);
        }
    }
}
