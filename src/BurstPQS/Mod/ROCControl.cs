using BurstPQS.Util;

namespace BurstPQS.Mod;

public class ROCControl : PQSROCControl, IBatchPQSMod
{
    public ROCControl(PQSROCControl mod)
    {
        CloneUtil.MemberwiseCopy(mod, this);
    }

    public void OnQuadBuildVertex(in QuadBuildData data)
    {
        allowROCScatter = data.allowScatter[data.VertexCount - 1];
    }

    public void OnQuadBuildVertexHeight(in QuadBuildData data) { }
}
