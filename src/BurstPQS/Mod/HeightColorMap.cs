using System;
using System.ComponentModel.Design;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

public class HeightColorMap : PQSMod_HeightColorMap, IBatchPQSMod
{
    public struct BurstLandClass(LandClass landClass)
    {
        public double altStart = landClass.altStart;
        public double altEnd = landClass.altEnd;
        public Color color = landClass.color;
        public bool lerpToNext = landClass.lerpToNext;
    }

    public BurstLandClass[] burstLandClasses;

    public HeightColorMap(PQSMod_HeightColorMap mod)
    {
        CloneUtil.MemberwiseCopy(mod, this);
    }

    public override void OnSetup()
    {
        base.OnSetup();

        burstLandClasses = new BurstLandClass[landClasses.Length];
        for (int i = 0; i < landClasses.Length; ++i)
            burstLandClasses[i] = new(landClasses[i]);
    }

    public virtual unsafe void OnQuadBuildVertex(in QuadBuildData data)
    {
        if (burstLandClasses is null)
            throw new NullReferenceException("burstLandClasses was null");

        fixed (BurstLandClass* pClasses = burstLandClasses)
        {
            BuildVertices(
                in data.burstData,
                new(pClasses, burstLandClasses.Length),
                sphere.radiusMin,
                sphere.radiusDelta,
                blend
            );
        }
    }

    public virtual void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        in BurstQuadBuildData data,
        in MemorySpan<BurstLandClass> classes,
        double radiusMin,
        double radiusDelta,
        float blend
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            var vHeight = (data.vertHeight[i] - radiusMin) / radiusDelta;
            var lcindex = SelectLandClassByHeight(classes, vHeight);
            ref readonly var lc = ref classes[lcindex];

            Color color = lc.color;
            if (lc.lerpToNext)
            {
                color = Color.Lerp(
                    lc.color,
                    classes[lcindex + 1].color,
                    (float)((vHeight - lc.altStart) / (lc.altEnd - lc.altStart))
                );
            }

            data.vertColor[i] = Color.Lerp(data.vertColor[i], color, blend);
        }
    }

    static int SelectLandClassByHeight(MemorySpan<BurstLandClass> classes, double height)
    {
        for (int i = 0; i < classes.Length; ++i)
        {
            if (height >= classes[i].altEnd && !(height > classes[i].altEnd))
                return i;
        }

        return Math.Min(classes.Length - 1, 0);
    }
}
