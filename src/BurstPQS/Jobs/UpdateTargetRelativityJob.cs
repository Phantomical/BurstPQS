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

struct SubdivisionTarget
{
    public double3 directionNormalized;
    public double absHeight;
    public double collapseFactor;
    public int maxLevelAtSpeed;
}

/// <summary>
/// Burst-compiled job that computes gcd1, gcDist, and subdivision actions from snapshot data.
/// </summary>
[BurstCompile]
struct ComputeSubdivisionJob : IJobParallelForBatch
{
    public double radius;

    [ReadOnly]
    public NativeArray<SubdivisionTarget> targets;

    [ReadOnly]
    public NativeArray<double> subdivisionThresholds;
    public int collapseLevels;
    public int maxLevel;
    public int minLevel;
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

            // A quad subdivides if any target wants it to, and only collapses
            // once every target agrees.
            var minG = double.PositiveInfinity;
            var minGcDist = double.PositiveInfinity;
            bool subdivide = !snap.isSubdivided && snap.subdivision < minLevel;
            bool collapse = snap.isSubdivided;

            for (int t = 0; t < targets.Length; t++)
            {
                var target = targets[t];
                var g =
                    math.acos(math.dot(snap.positionPlanetRelative, target.directionNormalized))
                    * radius
                    * 1.3;
                var gcDist = g + target.absHeight - snap.angularInterval;

                minG = math.min(minG, g);
                minGcDist = math.min(minGcDist, gcDist);

                if (snap.isSubdivided)
                    collapse &= ShouldCollapse(ref snap, ref target, gcDist);
                else
                    subdivide |= ShouldSubdivide(ref snap, ref target, gcDist);
            }

            results[i] = new QuadResult { gcd1 = minG, gcDist = minGcDist };

            if (snap.isSubdivided)
            {
                actions[i] = collapse ? SubdivisionAction.Collapse : SubdivisionAction.None;
            }
            else if (subdivide)
            {
                actions[i] = SubdivisionAction.Subdivide;
            }
            else
            {
                actions[i] = SubdivisionAction.None;

                bool shouldBeVisible = minG < visibleRadius;
                if (shouldBeVisible != snap.isVisible)
                    visibilityChangedQueue.Enqueue(i);
            }
        }
    }

    bool ShouldCollapse(ref QuadSnapshot q, ref SubdivisionTarget target, double gcDist)
    {
        return q.subdivision > maxLevel
            || q.subdivision >= collapseLevels
            || q.subdivision >= subdivisionThresholds.Length
            || gcDist
                > subdivisionThresholds[q.subdivision]
                    * target.collapseFactor
                    * q.subdivideThresholdFactor;
    }

    bool ShouldSubdivide(ref QuadSnapshot q, ref SubdivisionTarget target, double gcDist)
    {
        return q.subdivision < subdivisionThresholds.Length
            && gcDist < subdivisionThresholds[q.subdivision] * q.subdivideThresholdFactor
            && q.subdivision < target.maxLevelAtSpeed;
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
