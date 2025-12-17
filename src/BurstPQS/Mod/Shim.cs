using Unity.Jobs;

namespace BurstPQS.Mod;

public sealed class Shim(PQSMod mod) : BatchPQSMod
{
    readonly PQSMod mod = mod;

    class State(PQSMod mod) : BatchPQSModState
    {
        readonly PQSMod mod = mod;

        public override JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle)
        {
            handle.Complete();

            var vbData = PQS.vbData;
            vbData.buildQuad = data.buildQuad;
            vbData.gnomonicPlane = data.buildQuad.plane;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.CopyTo(vbData, i);
                mod.OnVertexBuildHeight(vbData);
                data.CopyFrom(vbData, i);
            }

            return handle;
        }

        public override JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle)
        {
            handle.Complete();

            var vbData = PQS.vbData;
            vbData.buildQuad = data.buildQuad;
            vbData.gnomonicPlane = data.buildQuad.plane;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                data.CopyTo(vbData, i);
                mod.OnVertexBuild(vbData);
                data.CopyFrom(vbData, i);
            }

            return handle;
        }

        public override void OnQuadBuilt(QuadBuildData data)
        {
            mod.OnQuadBuilt(data.buildQuad);
        }

        public override string ToString() => mod.ToString();
    }

    public override IBatchPQSModState OnQuadPreBuild(QuadBuildData data)
    {
        mod.OnQuadPreBuild(data.buildQuad);
        return new State(mod);
    }

    public override string ToString() => mod.ToString();
}
