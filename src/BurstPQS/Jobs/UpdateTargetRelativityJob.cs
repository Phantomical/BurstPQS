using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BurstPQS.Jobs;

struct QuadSnapshot
{
    public double3 positionPlanetRelative;
    public double angularInterval;
    public double subdivideThresholdFactor;
    public int subdivision;
    public bool isSubdivided;
    public bool isVisible;
    public bool hasOnUpdate;
}

struct QuadResult
{
    public double gcd1;
    public double gcDist;
}

/// <summary>
/// Gathers relevant fields from managed PQ objects into a NativeArray for Burst processing.
/// </summary>
struct GatherQuadDataJob : IJobParallelForBatch
{
    public ObjectHandle<List<PQ>> quads;

    [WriteOnly]
    public NativeArray<QuadSnapshot> snapshots;

    public void Execute(int start, int count)
    {
        var quadList = quads.Target;
        var end = start + count;

        for (int i = start; i < end; i++)
        {
            var q = quadList[i];
            var pos = q.positionPlanetRelative;

            snapshots[i] = new QuadSnapshot
            {
                positionPlanetRelative = new double3(pos.x, pos.y, pos.z),
                angularInterval = q.angularinterval,
                subdivideThresholdFactor = q.subdivideThresholdFactor,
                subdivision = q.subdivision,
                isSubdivided = q.isSubdivided,
                isVisible = q.isVisible,
                hasOnUpdate = q.onUpdate != null,
            };
        }
    }
}

/// <summary>
/// Burst-compiled job that computes gcd1, gcDist, and subdivision actions from snapshot data.
/// </summary>
[BurstCompile]
struct ComputeSubdivisionJob : IJobParallelForBatch
{
    public double3 relativeTargetPositionNormalized;
    public double radius;
    public double absTargetHeight;

    [ReadOnly]
    public NativeArray<double> subdivisionThresholds;

    [ReadOnly]
    public NativeArray<double> collapseThresholds;
    public int maxLevel;
    public int minLevel;
    public int maxLevelAtCurrentTgtSpeed;
    public double visibleRadius;

    [ReadOnly]
    public NativeArray<QuadSnapshot> snapshots;

    [WriteOnly]
    public NativeArray<SubdivisionAction> actions;

    [WriteOnly]
    public NativeArray<QuadResult> results;

    public NativeQueue<int>.ParallelWriter visibilityChangedQueue;

    public void Execute(int start, int count)
    {
        var end = start + count;

        for (int i = start; i < end; i++)
        {
            var snap = snapshots[i];

            var g =
                math.acos(math.dot(snap.positionPlanetRelative, relativeTargetPositionNormalized))
                * radius
                * 1.3;
            var gcDist = g + absTargetHeight - snap.angularInterval;

            results[i] = new QuadResult { gcd1 = g, gcDist = gcDist };

            if (snap.isSubdivided)
            {
                if (ShouldCollapse(ref snap, gcDist))
                    actions[i] = SubdivisionAction.Collapse;
                else
                    actions[i] = SubdivisionAction.None;
            }
            else
            {
                if (ShouldSubdivide(ref snap, gcDist))
                {
                    actions[i] = SubdivisionAction.Subdivide;
                }
                else
                {
                    actions[i] = SubdivisionAction.None;

                    bool shouldBeVisible = g < visibleRadius;
                    if (shouldBeVisible != snap.isVisible)
                        visibilityChangedQueue.Enqueue(i);
                }
            }
        }
    }

    bool ShouldCollapse(ref QuadSnapshot q, double gcDist)
    {
        return q.subdivision > maxLevel
            || q.subdivision >= collapseThresholds.Length
            || gcDist > collapseThresholds[q.subdivision] * q.subdivideThresholdFactor;
    }

    bool ShouldSubdivide(ref QuadSnapshot q, double gcDist)
    {
        if (q.subdivision < minLevel)
            return true;

        return q.subdivision < subdivisionThresholds.Length
            && gcDist < subdivisionThresholds[q.subdivision] * q.subdivideThresholdFactor
            && q.subdivision < maxLevelAtCurrentTgtSpeed;
    }
}

/// <summary>
/// Assigns computed gcd1/gcDist back to managed PQ objects.
/// </summary>
struct ScatterQuadResultsJob : IJobParallelForBatch
{
    public ObjectHandle<List<PQ>> quads;

    [ReadOnly]
    public NativeArray<QuadResult> results;

    public void Execute(int startIndex, int count)
    {
        var quadList = quads.Target;

        for (int i = startIndex; i < startIndex + count; i++)
        {
            var q = quadList[i];
            var r = results[i];
            q.gcd1 = r.gcd1;
            q.gcDist = r.gcDist;
        }
    }
}

/// <summary>
/// Burst-compiled job that collects indices of quads with onUpdate delegates.
/// </summary>
[BurstCompile]
struct CollectOnUpdateJob : IJob
{
    [ReadOnly]
    public NativeArray<QuadSnapshot> snapshots;

    public NativeList<int> onUpdateIndices;

    public void Execute()
    {
        for (int i = 0; i < snapshots.Length; i++)
            if (snapshots[i].hasOnUpdate)
                onUpdateIndices.Add(i);
    }
}
