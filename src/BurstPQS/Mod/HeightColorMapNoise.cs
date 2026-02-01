using System;
using BurstPQS.Collections;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_HeightColorMapNoise))]
public class HeightColorMapNoise(PQSMod_HeightColorMapNoise mod) : BatchPQSMod<PQSMod_HeightColorMapNoise>(mod)
{
    public struct BurstLandClass(PQSMod_HeightColorMapNoise.LandClass landClass)
    {
        public double altStart = landClass.altStart;
        public double altEnd = landClass.altEnd;
        public Color color = landClass.color;
        public bool lerpToNext = landClass.lerpToNext;
    }

    public BurstLandClass[] burstLandClasses;

    public override void OnSetup()
    {
        base.OnSetup();

        burstLandClasses = new BurstLandClass[mod.landClasses.Length];
        for (int i = 0; i < mod.landClasses.Length; ++i)
            burstLandClasses[i] = new(mod.landClasses[i]);
    }

    public override unsafe void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        if (burstLandClasses is null)
            throw new NullReferenceException("burstLandClasses was null");

        fixed (BurstLandClass* pClasses = burstLandClasses)
        {
            jobSet.Add(new BuildJob
            {
                classes = new(pClasses, burstLandClasses.Length),
                radiusMin = mod.sphere.radiusMin,
                radiusDelta = mod.sphere.radiusDelta,
                blend = mod.blend
            });
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct BuildJob : IBatchPQSVertexJob
    {
        public unsafe MemorySpan<BurstLandClass> classes;
        public double radiusMin;
        public double radiusDelta;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
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
}
