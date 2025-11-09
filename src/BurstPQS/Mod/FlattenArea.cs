using System;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class FlattenArea : BatchPQSMod<PQSMod_FlattenArea>
{
    public FlattenArea(PQSMod_FlattenArea mod)
        : base(mod) { }

    public override void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        if (!mod.overrideQuadBuildCheck && !mod.quadActive)
            return;

        var info = new BurstInfo
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
        };

        BuildHeights(in info, in data.burstData);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights([NoAlias] in BurstInfo info, [NoAlias] in BurstQuadBuildData data) =>
        info.Execute(in data);

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
