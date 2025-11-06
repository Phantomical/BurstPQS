using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightOffset : PQSMod_VertexHeightOffset, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        BuildHeights(data.vertHeight, offset);
    }

    [BurstCompile]
    [BurstPQSAutoPatch]
    static void BuildHeights([NoAlias] in MemorySpan<double> vertHeight, double offset)
    {
        for (int i = 0; i < vertHeight.Length; ++i)
            vertHeight[i] += offset;
    }
}
