using System;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_TangentTextureRanges))]
public class TangentTextureRanges(PQSMod_TangentTextureRanges mod)
    : BatchPQSMod<PQSMod_TangentTextureRanges>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                tangentX = PQSMod_TangentTextureRanges.tangentX,
                modulo = mod.modulo,
                lowStart = mod.lowStart,
                lowEnd = mod.lowEnd,
                highStart = mod.highStart,
                highEnd = mod.highEnd,
            }
        );
    }

    struct BuildJob : IBatchPQSVertexJob
    {
        public float[] tangentX;
        public double modulo;
        public double lowStart;
        public double lowEnd;
        public double highStart;
        public double highEnd;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var height = data.vertHeight[i];
                var low = 1.0 - SmoothStep(lowStart, lowEnd, height);
                var high = SmoothStep(highStart, highEnd, height);
                var med = 1.0 - low - high;

                low = Math.Round(low * modulo);
                med = Math.Round(med * modulo) * 2.0;
                high = Math.Round(high * modulo) * 3.0;

                tangentX[i] = (float)(high + med + low);
            }
        }

        static double SmoothStep(double a, double b, double x)
        {
            var t = MathUtil.Clamp01((x - a) / (b - a));
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
