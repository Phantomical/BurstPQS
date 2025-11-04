using BurstPQS.Util;

namespace BurstPQS.Mod;

public class FlattenOcean : PQSMod_FlattenOcean, IBatchPQSMod
{
    public FlattenOcean(PQSMod_FlattenOcean mod)
    {
        CloneUtil.MemberwiseCopy(mod, this);
    }

    public virtual void OnQuadBuildVertex(in QuadBuildData data) { }

    public virtual unsafe void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        // This one is simple enough that it is not worth using burst.
        var ptr = data.vertHeight.GetDataPtr();
        var count = data.VertexCount;

        for (int i = 0; i < count; ++i)
        {
            if (ptr[i] < oceanRad)
                ptr[i] = oceanRad;
        }
    }
}
