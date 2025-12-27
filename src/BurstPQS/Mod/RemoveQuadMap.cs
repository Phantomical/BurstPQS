using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_RemoveQuadMap))]
public class RemoveQuadMap(PQSMod_RemoveQuadMap mod) : BatchPQSMod<PQSMod_RemoveQuadMap>(mod)
{
    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data)
    {
        mod.OnQuadBuilt(data.buildQuad);
        return new State(mod);
    }

    class State(PQSMod_RemoveQuadMap mod) : BatchPQSModState<PQSMod_RemoveQuadMap>(mod)
    {
        NativeArray<bool> quadVisible = new(1, Allocator.TempJob);

        public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
        {
            var job = new BuildVerticesJob
            {
                data = data.burst,
                map = new(mod.map),
                minHeight = mod.minHeight,
                maxHeight = mod.maxHeight,
                quadVisible = quadVisible,
            };
            handle = job.Schedule(handle);
            job.map.Dispose(handle);

            return handle;
        }

        public override JobHandle OnQuadBuilt(QuadBuildData data)
        {
            mod.quadVisible = quadVisible[0];
            return base.OnQuadBuilt(data);
        }

        public override void Dispose()
        {
            quadVisible.Dispose();
        }
    }

    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public BurstMapSO map;
        public float minHeight;
        public float maxHeight;
        public NativeArray<bool> quadVisible;

        public void Execute()
        {
            bool visible = false;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var height = map.GetPixelFloat((float)data.u[i], (float)data.v[i]);
                if (height >= minHeight && height <= maxHeight)
                {
                    visible = true;
                    break;
                }
            }

            quadVisible[0] = visible;
        }
    }
}
