using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightMap : PQSMod_VertexHeightMap, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(heightMap, out var mapSO);
        BuildHeight(in data.burstData, in mapSO, heightMapOffset, heightMapDeformity);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildHeight(
        in BurstQuadBuildData data,
        in BurstMapSO heightMap,
        double heightMapOffset,
        double heightMapDeformity
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertHeight[i] +=
                heightMapOffset
                + heightMapDeformity * heightMap.GetPixelFloat(data.u[i], data.v[i]);
        }
    }
}
