using System;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_FlattenArea))]
public class FlattenArea(PQSMod_FlattenArea mod) : BatchPQSMod<PQSMod_FlattenArea>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        if (!mod.overrideQuadBuildCheck && !mod.quadActive)
        {
            // Reset stock mod state to match what OnQuadBuilt would set.
            // Without this, deferred builds leave the stock mod with
            // quadActive=false, which causes the stock OnVertexBuildHeight
            // to early-return when GetSurfaceHeight is called (e.g. by
            // PreciseUpdateSubQuadsPosition for quad repositioning).
            mod.quadActive = true;
            return;
        }

        jobSet.Add(
            new BuildJob
            {
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
            }
        );
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
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

        public readonly void BuildHeights(in BuildHeightsData data)
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
