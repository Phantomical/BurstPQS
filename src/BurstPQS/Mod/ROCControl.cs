using BurstPQS.Util;

namespace BurstPQS.Mod;

public class ROCControl : BatchPQSMod<PQSROCControl>
{
    public ROCControl(PQSROCControl mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildData data)
    {
        mod.allowROCScatter = data.allowScatter[data.VertexCount - 1];
    }
}
