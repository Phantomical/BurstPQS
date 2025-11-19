using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class RemoveQuadMap : BatchPQSModV1<PQSMod_RemoveQuadMap>
{
    public RemoveQuadMap(PQSMod_RemoveQuadMap mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(mod.map, out var mapSO);
        mod.quadVisible = ShouldBeVisible(
            in data.burstData,
            in mapSO,
            mod.minHeight,
            mod.maxHeight
        );
    }

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
