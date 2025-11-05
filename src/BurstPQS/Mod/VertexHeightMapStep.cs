namespace BurstPQS.Mod;

public class VertexHeightMapStep : PQSMod_VertexHeightMapStep, IBatchPQSMod
{
    public void OnQuadBuildVertex(in QuadBuildData data) { }

    public void OnQuadBuildVertexHeight(in QuadBuildData data)
    {
        // TODO: Accessing textures in burst is hard for now. Need to build
        //       something that allows working with the raw texture data.
        var vc = data.VertexCount;
        for (int i = 0; i < vc; ++i)
        {
            double h = heightMap.GetPixelBilinear((float)data.sx[i], (float)data.sy[i]).grayscale;
            if (h >= coastHeight)
                data.vertHeight[i] += heightMapOffset + h * heightDeformity;
            else
                data.vertHeight[i] += heightMapOffset;
        }
    }
}
