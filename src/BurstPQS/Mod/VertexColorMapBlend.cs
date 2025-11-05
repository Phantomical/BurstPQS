using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorMapBlend : PQSMod_VertexColorMapBlend, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        throw new System.NotImplementedException();
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(vertexColorMap, out var mapSO);
        SetVertexColors(in data.burstData, in mapSO, blend);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void SetVertexColors(
        in BurstQuadBuildData data,
        in BurstMapSO vertexColorMap,
        float blend
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertColor[i] = Color.Lerp(
                data.vertColor[i],
                vertexColorMap.GetPixelColor(data.u[i], data.v[i]),
                blend
            );
        }
    }
}
