using System;
using BurstPQS.Collections;
using Unity.Burst;
using Unity.Mathematics;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightOblate))]
public class VertexHeightOblate(PQSMod_VertexHeightOblate mod)
    : BatchPQSMod<PQSMod_VertexHeightOblate>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { height = mod.height, pow = mod.pow });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public double height;
        public double pow;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            const int stride = 4;

            int i = 0;
            for (; i <= data.VertexCount - stride; i += stride)
            {
                double4 v = data.v.GetVec4(i);
                double4 a = math.pow(math.sin(Math.PI * v), new(pow));
                double4 h = data.vertHeight.GetVec4(i);
                h += a * height;
                data.vertHeight.SetVec4(i, h);
            }

            for (; i < data.VertexCount; ++i)
            {
                double a = Math.Sin(Math.PI * data.v[i]);
                a = Math.Pow(a, pow);
                data.vertHeight[i] += a * height;
            }
        }
    }
}
