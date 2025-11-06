using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class RemoveQuadMap : PQSMod_RemoveQuadMap, IBatchPQSMod
{
    public RemoveQuadMap(PQSMod_RemoveQuadMap mod)
    {
        CloneUtil.MemberwiseCopy(mod, this);
    }

    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(map, out var mapSO);
        quadVisible = ShouldBeVisible(in data.burstData, in mapSO, minHeight, maxHeight);
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static bool ShouldBeVisible(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstMapSO map,
        float minHeight,
        float maxHeight
    )
    {
        bool visible = false;

        for (int i = 0; i < data.VertexCount; ++i)
        {
            var height = map.GetPixelFloat((float)data.u[i], (float)data.v[i]);
            if (height >= minHeight && height <= maxHeight)
                visible = true;
        }

        return visible;
    }
}
