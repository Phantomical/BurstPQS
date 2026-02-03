using System;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_HeightColorMap))]
public class HeightColorMap(PQSMod_HeightColorMap mod) : BatchPQSMod<PQSMod_HeightColorMap>(mod)
{
    public struct BurstLandClass(PQSMod_HeightColorMap.LandClass landClass)
    {
        public double altStart = landClass.altStart;
        public double altEnd = landClass.altEnd;
        public Color color = landClass.color;
        public bool lerpToNext = landClass.lerpToNext;
    }

    public NativeArray<BurstLandClass> burstLandClasses;

    public override void OnSetup()
    {
        base.OnSetup();

        burstLandClasses = new NativeArray<BurstLandClass>(
            mod.landClasses.Length,
            Allocator.Persistent
        );
        for (int i = 0; i < mod.landClasses.Length; ++i)
            burstLandClasses[i] = new(mod.landClasses[i]);
    }

    public override void Dispose()
    {
        burstLandClasses.Dispose();
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildVerticesJob { classes = burstLandClasses, blend = mod.blend });
    }

    [BurstCompile]
    struct BuildVerticesJob : IBatchPQSVertexJob
    {
        public NativeArray<BurstLandClass> classes;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var vHeight =
                    (data.vertHeight[i] - data.sphere.radiusMin) / data.sphere.radiusDelta;
                var lcindex = SelectLandClassByHeight(classes, vHeight);
                var lc = classes[lcindex];

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
    }

    static int SelectLandClassByHeight(NativeArray<BurstLandClass> classes, double height)
    {
        for (int i = 0; i < classes.Length; ++i)
        {
            if (height >= classes[i].altStart && height <= classes[i].altEnd)
                return i;
        }

        return Math.Max(classes.Length - 1, 0);
    }
}
