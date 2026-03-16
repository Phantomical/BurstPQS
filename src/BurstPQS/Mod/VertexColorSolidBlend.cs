using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorSolidBlend))]
public class VertexColorSolidBlend(PQSMod_VertexColorSolidBlend mod)
    : BatchPQSMod<PQSMod_VertexColorSolidBlend>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { color = mod.color });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob
    {
        public Color color;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            data.vertColor.Fill(color);
        }
    }
}
