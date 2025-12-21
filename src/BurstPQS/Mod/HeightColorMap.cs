using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_HeightColorMap))]
public class HeightColorMap : BatchPQSMod<PQSMod_HeightColorMap>, IBatchPQSModState
{
    public struct BurstLandClass(PQSMod_HeightColorMap.LandClass landClass)
    {
        public double altStart = landClass.altStart;
        public double altEnd = landClass.altEnd;
        public Color color = landClass.color;
        public bool lerpToNext = landClass.lerpToNext;
    }

    public NativeArray<BurstLandClass> burstLandClasses;

    public HeightColorMap(PQSMod_HeightColorMap mod)
        : base(mod) { }

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

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle) => handle;

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
    {
        var job = new BuildVerticesJob
        {
            data = data.burst,
            classes = burstLandClasses,
            blend = mod.blend,
        };

        return job.Schedule(handle);
    }

    public void OnQuadBuilt(QuadBuildData data) { }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public NativeArray<BurstLandClass> classes;
        public float blend;

        public void Execute()
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
