using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

// This seems to be backwards in the KSP source?
[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorSolid))]
public class VertexColorSolidBlend(PQSMod_VertexColorSolid mod) : BatchPQSMod<PQSMod_VertexColorSolid>(mod)
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
