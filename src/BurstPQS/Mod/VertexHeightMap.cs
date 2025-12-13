using BurstPQS.Util;
using Unity.Burst;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightMap))]
[BatchPQSShim]
public class VertexHeightMap(PQSMod_VertexHeightMap mod)
    : BatchPQSMod<PQSMod_VertexHeightMap>(mod),
        IBatchPQSModState
{
    [BurstCompile(FloatMode = FloatMode.Fast)]
    [BurstPQSAutoPatch]
    static void BuildHeight(
        [NoAlias] in BurstQuadBuildDataV1 data,
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

    public JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
    {
        var heightMap = new BurstMapSO(mod.heightMap);
        var job = new BuildHeightsJob
        {
            data = data.burst,
            heightMap = heightMap,
            heightMapOffset = mod.heightMapOffset,
            heightMapDeformity = mod.heightMapDeformity,
        };

        handle = job.Schedule(handle);
        heightMap.Dispose(handle);
        return handle;
    }

    public JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public void OnQuadBuilt(QuadBuildData data) { }

    struct BuildHeightsJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstMapSO heightMap;
        public double heightMapOffset;
        public double heightMapDeformity;

        public readonly void Execute()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.vertHeight[i] +=
                    heightMapOffset
                    + heightMapDeformity * heightMap.GetPixelFloat(data.u[i], data.v[i]);
            }
        }
    }
}
