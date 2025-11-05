using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexColorSolid : PQSMod_VertexColorSolid, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        BuildVertex(in data.burstData, in color, blend);
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVertex(in BurstQuadBuildData data, in Color color, float blend)
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertColor[i] = Color.Lerp(data.vertColor[i], color, blend);
        }
    }
}
