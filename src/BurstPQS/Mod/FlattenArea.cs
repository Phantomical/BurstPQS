using System;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_FlattenArea))]
[BatchPQSShim]
public class FlattenArea(PQSMod_FlattenArea mod)
    : BatchPQSMod<PQSMod_FlattenArea>(mod),
        IBatchPQSModState
{
    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        if (!mod.overrideQuadBuildCheck && !mod.quadActive)
            return handle;

        var job = new BuildHeightsJob
        {
            data = data.burst,
            DEBUG_showColors = mod.DEBUG_showColors,
            removeScatter = mod.removeScatter,
            posNorm = mod.posNorm,
            angleOuter = mod.angleOuter,
            angleInner = mod.angleInner,
            angleDelta = mod.angleDelta,
            angleQuadInclusion = mod.angleQuadInclusion,
            flattenToRadius = mod.flattenToRadius,
            smoothStart = mod.smoothStart,
            smoothEnd = mod.smoothEnd,
        };

        return job.Schedule(handle);
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public bool DEBUG_showColors;

        public bool removeScatter;
        public Vector3d posNorm;
        public double angleOuter;
        public double angleInner;
        public double angleDelta;
        public double angleQuadInclusion;
        public double flattenToRadius;
        public double smoothStart;
        public double smoothEnd;

        public readonly void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double testAngle = Math.Acos(Vector3d.Dot(data.directionFromCenter[i], posNorm));
                if (!(testAngle < angleQuadInclusion))
                    continue;

                if (removeScatter)
                    data.allowScatter[i] = false;
                if (DEBUG_showColors)
                    data.vertColor[i] = Color.green;
                if (!(testAngle < angleOuter))
                    continue;

                if (testAngle < angleInner)
                {
                    data.vertHeight[i] = flattenToRadius;
                    if (DEBUG_showColors)
                        data.vertColor[i] = Color.yellow;
                    continue;
                }

                double aDelta = (testAngle - angleInner) / angleDelta;
                data.vertHeight[i] = MathUtil.CubicHermite(
                    flattenToRadius,
                    data.vertHeight[i],
                    smoothStart,
                    smoothEnd,
                    aDelta
                );

                if (DEBUG_showColors)
                    data.vertColor[i] = Color.Lerp(Color.blue, Color.yellow, (float)aDelta);
            }
        }
    }
}
