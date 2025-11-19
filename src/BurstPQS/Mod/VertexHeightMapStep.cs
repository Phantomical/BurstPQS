namespace BurstPQS.Mod;

public class VertexHeightMapStep : BatchPQSModV1<PQSMod_VertexHeightMapStep>
{
    public VertexHeightMapStep(PQSMod_VertexHeightMapStep mod)
        : base(mod) { }

    public override void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        // TODO: Accessing textures in burst is hard for now. Need to build
        //       something that allows working with the raw texture data.
        var vc = data.VertexCount;
        for (int i = 0; i < vc; ++i)
        {
            double h = mod
                .heightMap.GetPixelBilinear((float)data.sx[i], (float)data.sy[i])
                .grayscale;
            if (h >= mod.coastHeight)
                data.vertHeight[i] += mod.heightMapOffset + h * mod.heightDeformity;
            else
                data.vertHeight[i] += mod.heightMapOffset;
        }
    }
}
