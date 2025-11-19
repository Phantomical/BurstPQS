namespace BurstPQS.Mod;

public sealed class InterfaceShim(IBatchPQSModV1 mod) : BatchPQSModV1
{
    readonly IBatchPQSModV1 mod = mod;

    public override void OnBatchVertexBuild(in QuadBuildDataV1 data) =>
        mod.OnBatchVertexBuild(in data);

    public override void OnBatchVertexBuildHeight(in QuadBuildDataV1 data) =>
        mod.OnBatchVertexBuildHeight(in data);
}
