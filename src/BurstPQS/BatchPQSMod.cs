using System.Collections.Generic;

namespace BurstPQS;

public interface IBatchPQSMod
{
    void OnQuadBuildVertex(in QuadBuildData data);

    void OnQuadBuildVertexHeight(in QuadBuildData data);
}

public abstract class BatchPQSMod : PQSMod, IBatchPQSMod
{
    public virtual void OnQuadBuildVertex(in QuadBuildData data) { }

    public virtual void OnQuadBuildVertexHeight(in QuadBuildData data) { }

    public override unsafe void OnVertexBuild(PQS.VertexBuildData data)
    {
        SingleVertexData vdata = default;
        vdata.CopyFrom(data);
        QuadBuildData qdata;
        qdata.buildQuad = data.buildQuad;
        qdata.burstData = new(data.buildQuad, &vdata, data.vertIndex);

        OnQuadBuildVertex(in qdata);

        vdata.CopyTo(data);
    }

    public override unsafe void OnVertexBuildHeight(PQS.VertexBuildData data)
    {
        SingleVertexData vdata = default;
        vdata.CopyFrom(data);
        QuadBuildData qdata;
        qdata.buildQuad = data.buildQuad;
        qdata.burstData = new(data.buildQuad, &vdata, data.vertIndex);

        OnQuadBuildVertexHeight(in qdata);

        vdata.CopyTo(data);
    }
}
