using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorSolid : BatchPQSModV1<PQSMod_VertexColorSolid>
{
    public VertexColorSolid(PQSMod_VertexColorSolid mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildDataV1 data)
    {
        BuildVertex(in data.burstData, in mod.color, mod.blend);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertex([NoAlias] in BurstQuadBuildDataV1 data, [NoAlias] in Color c, float blend)
    {
        var color = c;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertColor[i] = Color.Lerp(data.vertColor[i], color, blend);
        }
    }
}
