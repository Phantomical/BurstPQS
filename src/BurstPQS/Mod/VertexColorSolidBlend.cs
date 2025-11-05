using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

// This seems to be backwards in the KSP source?
[BurstCompile]
public class VertexColorSolidBlend : PQSMod_VertexColorSolid, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        BuildVertex(in data.burstData, in color);
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVertex(in BurstQuadBuildData data, in Color color)
    {
        data.vertColor.Fill(color);
    }
}
