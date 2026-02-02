using System;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_FlattenAreaTangential))]
public class FlattenAreaTangential(PQSMod_FlattenAreaTangential mod)
    : BatchPQSMod<PQSMod_FlattenAreaTangential>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        if (!mod.quadActive)
            return;

        jobSet.Add(
            new BuildJob
            {
                flattenToRadius = mod.flattenToRadius,
                smoothStart = mod.smoothStart,
                smoothEnd = mod.smoothEnd,
                angleInner = mod.angleInner,
                angleOuter = mod.angleOuter,
                angleDelta = mod.angleDelta,
                posNorm = mod.posNorm,
            }
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct BuildJob : IBatchPQSHeightJob
    {
        public double flattenToRadius;
        public double smoothStart;
        public double smoothEnd;
        public double angleInner;
        public double angleOuter;
        public double angleDelta;
        public Vector3d posNorm;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double testAngle = Math.Acos(Vector3d.Dot(data.directionFromCenter[i], posNorm));
                double vHeight = flattenToRadius / Math.Cos(testAngle);
                if (!(testAngle < angleOuter))
                    continue;

                if (testAngle < angleInner)
                {
                    data.vertHeight[i] = vHeight;
                }
                else
                {
                    double aDelta = (testAngle - angleInner) / angleDelta;
                    data.vertHeight[i] = MathUtil.CubicHermite(
                        vHeight,
                        data.vertHeight[i],
                        smoothStart,
                        smoothEnd,
                        aDelta
                    );
                }
            }
        }
    }
}
