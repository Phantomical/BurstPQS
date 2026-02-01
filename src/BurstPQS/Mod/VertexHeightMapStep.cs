using UnityEngine;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSMod_VertexHeightMapStep))]
public class VertexHeightMapStep(PQSMod_VertexHeightMapStep mod)
    : BatchPQSMod<PQSMod_VertexHeightMapStep>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                heightMap = mod.heightMap,
                coastHeight = mod.coastHeight,
                heightMapOffset = mod.heightMapOffset,
                heightDeformity = mod.heightDeformity,
            }
        );
    }

    struct BuildJob : IBatchPQSHeightJob
    {
        public Texture2D heightMap;
        public double coastHeight;
        public double heightMapOffset;
        public double heightDeformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                double h = heightMap
                    .GetPixelBilinear((float)data.sx[i], (float)data.sy[i])
                    .grayscale;
                if (h >= coastHeight)
                    data.vertHeight[i] += heightMapOffset + h * heightDeformity;
                else
                    data.vertHeight[i] += heightMapOffset;
            }
        }
    }
}
