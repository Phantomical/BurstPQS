using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

// This seems to be backwards in the KSP source?
[BurstCompile]
public class VertexColorSolidBlend : BatchPQSMod<PQSMod_VertexColorSolid>
{
    public VertexColorSolidBlend(PQSMod_VertexColorSolid mod)
        : base(mod) { }

    public override void OnQuadBuildVertex(in QuadBuildData data)
    {
        BuildVertex(in data.burstData, in mod.color);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildVertex([NoAlias] in BurstQuadBuildData data, in Color color)
    {
        data.vertColor.Fill(color);
    }
}
