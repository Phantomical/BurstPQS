using System;
using BurstPQS.Map;
using NiakoKerbalMods.NiakoKopernicus;
using Unity.Burst;
using Unity.Mathematics;

namespace BurstPQS.Niako;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexMitchellNetravaliHeightMap))]
class VertexMitchellNetravaliHeightMap(PQSMod_VertexMitchellNetravaliHeightMap mod)
    : BatchPQSMod<PQSMod_VertexMitchellNetravaliHeightMap>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                B = mod.B,
                C = mod.C,

                heightMap = BurstMapSO.Create(mod.heightMap),
                heightMapOffset = mod.heightMapOffset,
                heightMapDeformity = mod.heightMapDeformity,
            }
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct BuildJob : IBatchPQSHeightJob, IDisposable
    {
        public double B;
        public double C;

        public BurstMapSO heightMap;
        public double heightMapOffset;
        public double heightMapDeformity;

        public void BuildHeights(in BuildHeightsData data)
        {
            var mn = new MitchellNetravali(B, C);
            var wh = new int2(heightMap.Width, heightMap.Height);

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var uv = new double2(data.u[i], data.v[i]);
                var uvWh = uv * wh;
                var xy0 = (int2)math.floor(uvWh);
                var uvD = uvWh - (double2)xy0;

                double4 PX = default;
                double4 PY = default;

                for (int iy = -1; iy < 3; ++iy)
                {
                    int y = math.clamp(xy0.y + iy, 0, wh.y);
                    for (int ix = -1; ix < 3; ++ix)
                    {
                        int x = ClampLoop(xy0.x + ix, 0, wh.x);
                        PX[ix + 1] = heightMap.GetPixelFloat(x, y);
                    }

                    PY[iy + 1] = mn.Evaluate(PX.x, PX.y, PX.z, PX.w, uvD.x);
                }

                var value = mn.Evaluate(PY.x, PY.y, PY.z, PY.w, uvD.y);
                data.vertHeight[i] += heightMapOffset + heightMapDeformity * value;
            }
        }

        public void Dispose()
        {
            heightMap.Dispose();
        }

        /// <summary>
        /// Clamp an <see cref="int"/> between two values, but looping around if a limit is reached
        /// (similar to how angles work)
        /// </summary>
        static int ClampLoop(int value, int min, int max)
        {
            int d = max - min;
            if (value < min)
                return value + d;
            if (value >= max)
                return value - d;
            return value;
        }
    }
}
