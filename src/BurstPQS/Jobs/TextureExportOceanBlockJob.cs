using System;
using System.Runtime.CompilerServices;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Jobs;

/// <summary>
/// Computes heights and tangent-space normals for a block of the ocean sphere.
/// Unlike <see cref="TextureExportBlockJob"/>, this skips vertex colors.
/// </summary>
internal struct TextureExportOceanBlockJob() : IJob
{
    public ObjectHandle<BatchPQSJobSet> jobSet;
    public SphereData sphere;

    public int resX;
    public int resY;

    public int startX;
    public int startY;

    public int blockW;
    public int blockH;

    [WriteOnly]
    public NativeArray<float> blockHeights;

    [WriteOnly]
    public NativeArray<Vector3> blockNormals;

    static TextureExportOceanBlockJob()
    {
        TextureExportOceanBlockJobExt.Init();
    }

    public void Execute()
    {
        var js = jobSet.Target;

        int sideW = blockW + 1;
        int sideH = blockH + 1;
        int gridSize = sideW * sideH;

        var heightData = new BuildHeightsData(sphere, gridSize);

        this.InitGridData(ref heightData, sideW, sideH);

        js.BuildHeights(in heightData);

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

        for (int ly = 0; ly < blockH; ly++)
        {
            for (int lx = 0; lx < blockW; lx++)
            {
                int gIdx = ly * sideW + lx;
                int oIdx = ly * blockW + lx;

                blockHeights[oIdx] = (float)(heightData.vertHeight[gIdx] - radius);

                var dir = heightData.directionFromCenter[gIdx].Normalized();
                var dirRight = heightData.directionFromCenter[gIdx + 1];
                var dirDown = heightData.directionFromCenter[gIdx + sideW];

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

                double h = heightData.vertHeight[gIdx];
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

/// <summary>
/// Blends ocean data into terrain block outputs. For each pixel where the ocean
/// height exceeds the terrain height, the terrain height, normal, and color are
/// replaced with the ocean values.
/// </summary>
[BurstCompile]
internal struct TextureExportBlendOceanJob : IJob
{
    // Terrain outputs (modified in place).
    public NativeArray<float> blockHeights;
    public NativeArray<Vector3> blockNormals;
    public NativeArray<Color> blockColors;

    // Ocean outputs (read-only).
    [ReadOnly]
    public NativeArray<float> oceanHeights;

    [ReadOnly]
    public NativeArray<Vector3> oceanNormals;

    public Color oceanColor;

    public void Execute()
    {
        for (int i = 0; i < blockHeights.Length; i++)
        {
            if (oceanHeights[i] > blockHeights[i])
            {
                blockHeights[i] = oceanHeights[i];
                blockNormals[i] = oceanNormals[i];
                blockColors[i] = oceanColor;
            }
        }
    }
}

#pragma warning disable CS8500
[BurstCompile]
internal static unsafe class TextureExportOceanBlockJobExt
{
    delegate void InitGridDataDelegate(
        TextureExportOceanBlockJob* job,
        BuildHeightsData* data,
        int sideW,
        int sideH
    );

    delegate void BuildBlockOutputDelegate(
        TextureExportOceanBlockJob* job,
        BuildHeightsData* data,
        int sideW
    );

    static readonly InitGridDataDelegate InitGridDataFunc;
    static readonly BuildBlockOutputDelegate BuildBlockOutputFunc;

    static TextureExportOceanBlockJobExt()
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
        this ref TextureExportOceanBlockJob job,
        ref BuildHeightsData data,
        int sideW,
        int sideH
    )
    {
        fixed (TextureExportOceanBlockJob* pjob = &job)
        fixed (BuildHeightsData* pdata = &data)
        {
            InitGridDataFunc(pjob, pdata, sideW, sideH);
        }
    }

    internal static void BuildBlockOutput(
        this ref TextureExportOceanBlockJob job,
        ref BuildHeightsData data,
        int sideW
    )
    {
        fixed (TextureExportOceanBlockJob* pjob = &job)
        fixed (BuildHeightsData* pdata = &data)
        {
            BuildBlockOutputFunc(pjob, pdata, sideW);
        }
    }

    [BurstCompile]
    static void InitGridDataBurst(
        TextureExportOceanBlockJob* job,
        BuildHeightsData* data,
        int sideW,
        int sideH
    ) =>
        Unsafe
            .AsRef<TextureExportOceanBlockJob>(job)
            .InitGridDataImpl(ref Unsafe.AsRef<BuildHeightsData>(data), sideW, sideH);

    [BurstCompile]
    static void BuildBlockOutputBurst(
        TextureExportOceanBlockJob* job,
        BuildHeightsData* data,
        int sideW
    ) =>
        Unsafe
            .AsRef<TextureExportOceanBlockJob>(job)
            .BuildBlockOutputImpl(ref Unsafe.AsRef<BuildHeightsData>(data), sideW);
}
#pragma warning restore CS8500
