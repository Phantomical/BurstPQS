using System;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class FlattenArea : PQSMod_FlattenArea, IBatchPQSMod
{
    public FlattenArea(PQSMod_FlattenArea mod)
    {
        CloneUtil.MemberwiseCopy(mod, this);
    }

    public virtual void OnQuadBuildVertex(in QuadBuildData data) { }

    public virtual void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        if (!overrideQuadBuildCheck && !quadActive)
            return;

        var info = new BurstInfo
        {
            DEBUG_showColors = DEBUG_showColors,
            removeScatter = removeScatter,
            posNorm = posNorm,
            angleOuter = angleOuter,
            angleInner = angleInner,
            angleDelta = angleDelta,
            angleQuadInclusion = angleQuadInclusion,
            flattenToRadius = flattenToRadius,
            smoothStart = smoothStart,
            smoothEnd = smoothEnd,
        };

        SetHeight(in info, in data.burstData);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void SetHeight(in BurstInfo info, in BurstQuadBuildData data) => info.Execute(in data);

    struct BurstInfo
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

        public readonly void Execute(in BurstQuadBuildData data)
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
                data.vertHeight[i] = CubicHermite(
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

        public static double CubicHermite(
            double start,
            double end,
            double startTangent,
            double endTangent,
            double t
        )
        {
            double ct2 = t * t;
            double ct3 = ct2 * t;
            return start * (2.0 * ct3 - 3.0 * ct2 + 1.0)
                + startTangent * (ct3 - 2.0 * ct2 + t)
                + end * (-2.0 * ct3 + 3.0 * ct2)
                + endTangent * (ct3 - ct2);
        }
    }
}
