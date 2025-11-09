using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
public class VertexHeightMap : BatchPQSMod<PQSMod_VertexHeightMap>
{
    public VertexHeightMap(PQSMod_VertexHeightMap mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        using var guard = BurstMapSO.Create(mod.heightMap, out var mapSO);
        BuildHeight(in data.burstData, in mapSO, mod.heightMapOffset, mod.heightMapDeformity);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeight(
        [NoAlias] in BurstQuadBuildData data,
        [NoAlias] in BurstMapSO heightMap,
        double heightMapOffset,
        double heightMapDeformity
    )
    {
        for (int i = 0; i < data.VertexCount; ++i)
        {
            data.vertHeight[i] +=
                heightMapOffset
                + heightMapDeformity * heightMap.GetPixelFloat(data.u[i], data.v[i]);
        }
    }
}
