using Mono.Cecil.Mdb;
using Unity.Jobs;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSROCControl))]
public class ROCControl(PQSROCControl mod) : InlineBatchPQSMod<PQSROCControl>(mod)
{
    public override JobHandle OnQuadBuilt(QuadBuildData data)
    {
        mod.allowROCScatter = data.allowScatter[data.VertexCount - 1];
        return base.OnQuadBuilt(data);
    }
}
