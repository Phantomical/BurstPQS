using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorMap : PQSMod_VertexColorMap, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        throw new System.NotImplementedException();
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(vertexColorMap, out var mapSO);
        SetVertexColors(in data.burstData, in mapSO);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void SetVertexColors(in BurstQuadBuildData data, in BurstMapSO vertexColorMap)
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertColor[i] = vertexColorMap.GetPixelColor(data.u[i], data.v[i]);
        }
    }
}
