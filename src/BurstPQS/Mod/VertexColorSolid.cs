using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexColorSolid))]
public class VertexColorSolid(PQSMod_VertexColorSolid mod) : BatchPQSMod<PQSMod_VertexColorSolid>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { color = mod.color, blend = mod.blend });
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct BuildJob : IBatchPQSVertexJob
    {
        public Color color;
        public float blend;

        public readonly void BuildVertices(in BuildVerticesData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertColor[i] = Color.Lerp(data.vertColor[i], color, blend);
            }
        }
    }
}
