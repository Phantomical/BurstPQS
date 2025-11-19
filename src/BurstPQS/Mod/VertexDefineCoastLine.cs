using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexDefineCoastLine : BatchPQSModV1<PQSMod_VertexDefineCoastLine>
{
    public VertexDefineCoastLine(PQSMod_VertexDefineCoastLine mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        BuildHeight(in data.burstData, mod.oceanRadius, mod.depthOffset);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeight(
        [NoAlias] in BurstQuadBuildData data,
        double oceanRadius,
        double depthOffset
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            if (data.vertHeight[i] < oceanRadius)
                data.vertHeight[i] -= depthOffset;
        }
    }
}
