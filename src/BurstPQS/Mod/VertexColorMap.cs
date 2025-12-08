using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorMap : BatchPQSModV1<PQSMod_VertexColorMap>
{
    public VertexColorMap(PQSMod_VertexColorMap mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildDataV1 data)
    {
        using var mapSO = new BurstMapSO(mod.vertexColorMap);
        BuildVertices(in data.burstData, in mapSO);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildDataV1 data,
        [NoAlias] in BurstMapSO vertexColorMap
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertColor[i] = vertexColorMap.GetPixelColor(data.u[i], data.v[i]);
        }
    }
}
