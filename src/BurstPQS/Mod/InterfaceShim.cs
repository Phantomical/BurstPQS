namespace BurstPQS.Mod;

public sealed class InterfaceShim(IBatchPQSModV1 mod) : BatchPQSModV1
{
    readonly IBatchPQSModV1 mod = mod;

    public override void OnBatchVertexBuild(in QuadBuildData data) =>
        mod.OnBatchVertexBuild(in data);

    public override void OnBatchVertexBuildHeight(in QuadBuildData data) =>
        mod.OnBatchVertexBuildHeight(in data);
}
