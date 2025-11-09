using System;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class FlattenOcean : BatchPQSMod<PQSMod_FlattenOcean>
{
    public FlattenOcean(PQSMod_FlattenOcean mod)
        : base(mod) { }

    public override void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        BuildHeights(data.vertHeight, mod.oceanRad);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights([NoAlias] in MemorySpan<double> vertHeight, double oceanRad)
    {
        for (int i = 0; i < vertHeight.Length; ++i)
            vertHeight[i] = Math.Max(vertHeight[i], oceanRad);
    }
}
