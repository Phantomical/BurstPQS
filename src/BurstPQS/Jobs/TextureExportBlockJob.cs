using System;
using System.Runtime.CompilerServices;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Jobs;

/// <summary>
/// Computes heights, tangent-space normals, and vertex colors for a 128x128 block
/// of an equirectangular texture by running the BurstPQS mod pipeline.
/// </summary>
internal struct TextureExportBlockJob() : IJob
{
    // Shared across all block jobs — caller is responsible for disposal.
    public ObjectHandle<BatchPQSJobSet> jobSet;
    public SphereData sphere;

    /// <summary>Full texture resolution.</summary>
    public int resX;
    public int resY;

    /// <summary>Top-left pixel coordinate of this block in the full texture.</summary>
    public int startX;
    public int startY;

    /// <summary>Actual block dimensions (may be smaller than 128 at texture edges).</summary>
    public int blockW;
    public int blockH;

    // Per-block output arrays, sized blockW * blockH. Allocated by the caller.
    [WriteOnly]
    public NativeArray<float> blockHeights;

    [WriteOnly]
    public NativeArray<Vector3> blockNormals;

    [WriteOnly]
    public NativeArray<Color> blockColors;

    static TextureExportBlockJob()
    {
        TextureExportBlockJobExt.Init();
    }

    public void Execute()
    {
        var js = jobSet.Target;

        // We need a (blockW+1) x (blockH+1) grid of heights and directions
        // so that normals at the right/bottom edge have neighbor data.
        int sideW = blockW + 1;
        int sideH = blockH + 1;
        int gridSize = sideW * sideH;

        var heightData = new BuildHeightsData(sphere, gridSize);
        var vertexData = new BuildVerticesData(heightData);

        this.InitGridData(ref heightData, sideW, sideH);

        js.BuildHeights(in heightData);

        vertexData.u2.Clear();
        vertexData.v2.Clear();
        vertexData.u3.Clear();
        vertexData.v3.Clear();
        vertexData.u4.Clear();
        vertexData.v4.Clear();

        js.BuildVertices(in vertexData);

        this.BuildBlockOutput(ref heightData, sideW);
    }

    #region InitGridData
    internal void InitGridDataImpl(ref BuildHeightsData heightData, int sideW, int sideH)
    {
        for (int r = 0; r < sideH; r++)
        {
            int globalY = Math.Min(startY + r, resY - 1);
            double lat = Math.PI / 2.0 - Math.PI * globalY / resY;
            double cosLat = Math.Cos(lat);
            double sinLat = Math.Sin(lat);

            for (int c = 0; c < sideW; c++)
            {
                int i = r * sideW + c;
                int globalX = (startX + c) % resX;

                double lon = 2.0 * Math.PI * globalX / resX;
                var dir = new Vector3d(cosLat * Math.Sin(lon), sinLat, cosLat * Math.Cos(lon));

                heightData.directionFromCenter[i] = dir;

                // PQS-convention longitude (matches BuildQuadJob.InitHeightDataImpl)
                var dirXZ = new Vector3d(dir.x, 0.0, dir.z);
                double pqsLon;
                if (dirXZ.sqrMagnitude == 0.0)
                    pqsLon = 0.0;
                else if (dirXZ.z < 0.0)
                    pqsLon = Math.PI - Math.Asin(dirXZ.x / dirXZ.magnitude);
                else
                    pqsLon = Math.Asin(dirXZ.x / dirXZ.magnitude);

                heightData.latitude[i] = lat;
                heightData.longitude[i] = pqsLon;

                double u = pqsLon / Math.PI * 0.5;
                double v = lat / Math.PI + 0.5;
                heightData.u[i] = u;
                heightData.v[i] = v;
                heightData.sx[i] = u < 0 ? u + 1.0 : u;
                heightData.sy[i] = v;
            }
        }

        heightData.vertHeight.Fill(heightData.sphere.radius);
        heightData.vertColor.Clear();
        heightData.allowScatter.Fill(true);
    }
    #endregion

    #region BuildBlockOutput
    internal void BuildBlockOutputImpl(ref BuildHeightsData heightData, int sideW)
    {
        double radius = heightData.sphere.radius;

        // Copy colors for the inner blockW x blockH region.
        for (int r = 0; r < blockH; r++)
        {
            for (int c = 0; c < blockW; c++)
                blockColors[r * blockW + c] = heightData.vertColor[r * sideW + c];
        }

        // Compute tangent-space normals and copy heights (as altitude relative to radius).
        for (int ly = 0; ly < blockH; ly++)
        {
            for (int lx = 0; lx < blockW; lx++)
            {
                int gIdx = ly * sideW + lx;
                int oIdx = ly * blockW + lx;

                double h = heightData.vertHeight[gIdx];
                blockHeights[oIdx] = (float)(h - radius);

                // Neighbors (right and down) are at +1 in the grid, which is valid
                // because the grid is (blockW+1) x (blockH+1).
                var dir = heightData.directionFromCenter[gIdx].Normalized();
                var dirRight = heightData.directionFromCenter[gIdx + 1];
                var dirDown = heightData.directionFromCenter[gIdx + sideW];

                // Build tangent space from the sphere surface at this point.
                var tangentX = (Vector3)(radius * dirRight - radius * dir).Normalized();
                var tangentY = (Vector3)Vector3d.Cross(dir, tangentX).Normalized();
                var normal = (Vector3)dir;

                var tbn = new Matrix4x4(
                    new Vector4(tangentX.x, tangentX.y, tangentX.z, 0f),
                    new Vector4(tangentY.x, tangentY.y, tangentY.z, 0f),
                    new Vector4(normal.x, normal.y, normal.z, 0f),
                    new Vector4(0f, 0f, 0f, 1f)
                );
                tbn = Matrix4x4.Inverse(tbn);

                // World-space normal from height-displaced positions.
                var pos = dir * h;
                var posRight = dirRight * heightData.vertHeight[gIdx + 1];
                var posDown = dirDown * heightData.vertHeight[gIdx + sideW];

                var edge1 = (posRight - pos).Normalized();
                var edge2 = (posDown - pos).Normalized();
                var worldNormal = (Vector3)Vector3d.Cross(edge1, edge2).Normalized();

                blockNormals[oIdx] = Vector3.Normalize(tbn.MultiplyVector(worldNormal));
            }
        }
    }
    #endregion
}

#pragma warning disable CS8500
[BurstCompile]
internal static unsafe class TextureExportBlockJobExt
{
    delegate void InitGridDataDelegate(
        TextureExportBlockJob* job,
        BuildHeightsData* data,
        int sideW,
        int sideH
    );

    delegate void BuildBlockOutputDelegate(
        TextureExportBlockJob* job,
        BuildHeightsData* data,
        int sideW
    );

    static readonly InitGridDataDelegate InitGridDataFunc;
    static readonly BuildBlockOutputDelegate BuildBlockOutputFunc;

    static TextureExportBlockJobExt()
    {
        InitGridDataFunc = BurstUtil
            .MaybeCompileFunctionPointer<InitGridDataDelegate>(InitGridDataBurst)
            .Invoke;

        BuildBlockOutputFunc = BurstUtil
            .MaybeCompileFunctionPointer<BuildBlockOutputDelegate>(BuildBlockOutputBurst)
            .Invoke;
    }

    internal static void Init() { }

    internal static void InitGridData(
        this ref TextureExportBlockJob job,
        ref BuildHeightsData data,
        int sideW,
        int sideH
    )
    {
        fixed (TextureExportBlockJob* pjob = &job)
        fixed (BuildHeightsData* pdata = &data)
        {
            InitGridDataFunc(pjob, pdata, sideW, sideH);
        }
    }

    internal static void BuildBlockOutput(
        this ref TextureExportBlockJob job,
        ref BuildHeightsData data,
        int sideW
    )
    {
        fixed (TextureExportBlockJob* pjob = &job)
        fixed (BuildHeightsData* pdata = &data)
        {
            BuildBlockOutputFunc(pjob, pdata, sideW);
        }
    }

    [BurstCompile]
    static void InitGridDataBurst(
        TextureExportBlockJob* job,
        BuildHeightsData* data,
        int sideW,
        int sideH
    ) =>
        Unsafe
            .AsRef<TextureExportBlockJob>(job)
            .InitGridDataImpl(ref Unsafe.AsRef<BuildHeightsData>(data), sideW, sideH);

    [BurstCompile]
    static void BuildBlockOutputBurst(
        TextureExportBlockJob* job,
        BuildHeightsData* data,
        int sideW
    ) =>
        Unsafe
            .AsRef<TextureExportBlockJob>(job)
            .BuildBlockOutputImpl(ref Unsafe.AsRef<BuildHeightsData>(data), sideW);
}
#pragma warning restore CS8500
