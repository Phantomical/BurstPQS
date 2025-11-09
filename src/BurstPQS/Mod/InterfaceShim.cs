namespace BurstPQS.Mod;

public sealed class InterfaceShim(IBatchPQSMod mod) : BatchPQSMod
{
    readonly IBatchPQSMod mod = mod;

    public override void OnBatchVertexBuild(in QuadBuildData data) =>
        mod.OnBatchVertexBuild(in data);

    public override void OnBatchVertexBuildHeight(in QuadBuildData data) =>
        mod.OnBatchVertexBuildHeight(in data);
}
