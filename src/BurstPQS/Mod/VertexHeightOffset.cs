using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightOffset : PQSMod_VertexHeightOffset, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public unsafe void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        // This is small enough that it is probably not worth burst-compiling.
        var vertHeight = data.vertHeight.GetDataPtr();
        var vc = data.VertexCount;
        var offset = this.offset;

        for (int i = 0; i < vc; ++i)
            vertHeight[i] += offset;
    }
}
