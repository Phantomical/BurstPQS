using System;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightOblate))]
public class VertexHeightOblate(PQSMod_VertexHeightOblate mod) : BatchPQSMod<PQSMod_VertexHeightOblate>(mod)
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
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double a = Math.Sin(Math.PI * data.v[i]);
                a = Math.Pow(a, pow);
                data.vertHeight[i] += a * height;
            }
        }
    }
}
