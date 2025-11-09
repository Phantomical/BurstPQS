using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class GnomonicTest : BatchPQSMod<PQSMod_GnomonicTest>
{
    public GnomonicTest(PQSMod_GnomonicTest mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildData data)
    {
        BuildVertices(in data.burstData);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices([NoAlias] in BurstQuadBuildData data)
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            float r = 0f;
            float g = 0f;
            float b = 0f;

            ref readonly var gnomonicUVs = ref data.gnomonicUVs[i];
            if (gnomonicUVs[0].acceptable)
            {
                r += (float)gnomonicUVs[0].gnomonicU;
                g += (float)gnomonicUVs[0].gnomonicV;
            }
            if (gnomonicUVs[1].acceptable)
            {
                r += (float)gnomonicUVs[1].gnomonicU;
                g += (float)gnomonicUVs[1].gnomonicV;
            }
            if (gnomonicUVs[2].acceptable)
            {
                g += (float)gnomonicUVs[2].gnomonicU;
                b += (float)gnomonicUVs[2].gnomonicV;
            }
            if (gnomonicUVs[3].acceptable)
            {
                g += (float)gnomonicUVs[3].gnomonicU;
                b += (float)gnomonicUVs[3].gnomonicV;
            }
            if (gnomonicUVs[4].acceptable)
            {
                b += (float)gnomonicUVs[4].gnomonicU;
                r += (float)gnomonicUVs[4].gnomonicV;
            }
            if (gnomonicUVs[5].acceptable)
            {
                b += (float)gnomonicUVs[5].gnomonicU;
                r += (float)gnomonicUVs[5].gnomonicV;
            }

            data.vertColor[i] = new(Mathf.Min(r, 1f), Mathf.Min(g, 1f), Mathf.Min(b, 1f));
        }
    }
}
