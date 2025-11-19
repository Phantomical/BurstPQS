using System;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class FlattenAreaTangential : BatchPQSModV1<PQSMod_FlattenAreaTangential>
{
    public FlattenAreaTangential(PQSMod_FlattenAreaTangential mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        if (!mod.quadActive)
            return;

        var info = new BurstInfo
        {
            DEBUG_showColors = mod.DEBUG_showColors,
            flattenToRadius = mod.flattenToRadius,
            smoothStart = mod.smoothStart,
            smoothEnd = mod.smoothEnd,
            angleInner = mod.angleInner,
            angleOuter = mod.angleOuter,
            angleQuadInclusion = mod.angleQuadInclusion,
            angleDelta = mod.angleDelta,
            posNorm = mod.posNorm,
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

        public double flattenToRadius;
        public double smoothStart;
        public double smoothEnd;
        public double angleInner;
        public double angleOuter;
        public double angleQuadInclusion;
        public double angleDelta;
        public Vector3d posNorm;

        public readonly void Execute(in BurstQuadBuildData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                if (DEBUG_showColors)
                    data.vertColor[i] = Color.green;

                double testAngle = Math.Acos(Vector3d.Dot(data.directionFromCenter[i], posNorm));
                double vHeight = flattenToRadius / Math.Cos(testAngle);
                if (!(testAngle < angleOuter))
                    return;

                if (testAngle < angleInner)
                {
                    data.vertHeight[i] = vHeight;
                    if (DEBUG_showColors)
                        data.vertColor[i] = Color.yellow;
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

                    if (DEBUG_showColors)
                    {
                        data.vertColor[i] = Color.Lerp(
                            Color.blue,
                            Color.yellow,
                            (float)(1.0 - aDelta)
                        );
                    }
                }
            }
        }
    }
}
