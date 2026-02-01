namespace BurstPQS.Mod;

public sealed class Shim(PQSMod mod) : BatchPQSMod
{
    readonly PQSMod mod = mod;

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        mod.OnQuadPreBuild(quad);

        jobSet.Add(new ShimJob { mod = mod, quad = quad });
    }

    struct ShimJob : IBatchPQSHeightJob, IBatchPQSVertexJob
    {
        public PQSMod mod;
        public PQ quad;

        public void BuildHeights(in BuildHeightsData data)
        {
            var vbData = PQS.vbData;
            vbData.buildQuad = quad;
            vbData.gnomonicPlane = quad.plane;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                vbData.vertIndex = i;
                vbData.directionFromCenter = data.directionFromCenter[i];
                vbData.vertHeight = data.vertHeight[i];
                vbData.u = data.u[i];
                vbData.v = data.v[i];
                vbData.longitude = data.longitude[i];
                vbData.latitude = data.latitude[i];

                mod.OnVertexBuildHeight(vbData);

                data.vertHeight[i] = vbData.vertHeight;
                data.directionFromCenter[i] = vbData.directionFromCenter;
            }
        }

        public void BuildVertices(in BuildVerticesData data)
        {
            var vbData = PQS.vbData;
            vbData.buildQuad = quad;
            vbData.gnomonicPlane = quad.plane;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                vbData.vertIndex = i;
                vbData.directionFromCenter = data.directionFromCenter[i];
                vbData.vertHeight = data.vertHeight[i];
                vbData.vertColor = data.vertColor[i];
                vbData.u = data.u[i];
                vbData.v = data.v[i];
                vbData.u2 = data.u2[i];
                vbData.v2 = data.v2[i];
                vbData.u3 = data.u3[i];
                vbData.v3 = data.v3[i];
                vbData.u4 = data.u4[i];
                vbData.v4 = data.v4[i];
                vbData.allowScatter = data.allowScatter[i];
                vbData.longitude = data.longitude[i];
                vbData.latitude = data.latitude[i];

                mod.OnVertexBuild(vbData);

                data.vertColor[i] = vbData.vertColor;
                data.allowScatter[i] = vbData.allowScatter;
            }
        }
    }

    public override string ToString() => mod.ToString();
}
