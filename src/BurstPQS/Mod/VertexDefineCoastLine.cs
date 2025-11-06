using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexDefineCoastLine : PQSMod_VertexDefineCoastLine, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        BuildHeight(in data.burstData, oceanRadius, depthOffset);
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
