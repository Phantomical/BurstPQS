using BurstPQS.Noise;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexVoronoi : PQSMod_VertexVoronoi, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        BuildHeights(in data.burstData, new(voronoi), deformation);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeights(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstVoronoi voronoi,
        double deformation
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertHeight[i] += voronoi.GetValue(data.directionFromCenter[i]) * deformation;
        }
    }
}
