using BurstPQS.Util;

namespace BurstPQS.Mod;

public class ROCControl : BatchPQSModV1<PQSROCControl>
{
    public ROCControl(PQSROCControl mod)
        : base(mod) { }

    public override void OnBatchVertexBuild(in QuadBuildDataV1 data)
    {
        mod.allowROCScatter = data.allowScatter[data.VertexCount - 1];
    }
}
