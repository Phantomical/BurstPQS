using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorMap : BatchPQSMod<PQSMod_VertexColorMap>
{
    public VertexColorMap(PQSMod_VertexColorMap mod)
        : base(mod) { }

    public override void OnQuadBuildVertex(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(mod.vertexColorMap, out var mapSO);
        BuildVertices(in data.burstData, in mapSO);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildVertices(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstMapSO vertexColorMap
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertColor[i] = vertexColorMap.GetPixelColor(data.u[i], data.v[i]);
        }
    }
}
