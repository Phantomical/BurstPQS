using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightOffset : BatchPQSModV1<PQSMod_VertexHeightOffset>
{
    public VertexHeightOffset(PQSMod_VertexHeightOffset mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data)
    {
        BuildHeights(data.vertHeight, mod.offset);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildHeights([NoAlias] in MemorySpan<double> vertHeight, double offset)
    {
        for (int i = 0; i < vertHeight.Length; ++i)
            vertHeight[i] += offset;
    }
}
