using System;
using BurstPQS.Tools;
using BurstPQS.Util;
using UnityEngine;

namespace BurstPQS.Jobs;

/// <summary>
/// Maps exported texture pixels onto directions on the unit sphere and fills in the per-vertex
/// inputs that the PQS mod pipeline reads.
/// </summary>
internal static class TextureExportGrid
{
    /// <summary>
    /// Fills the first <paramref name="sideW"/> x <paramref name="sideH"/> entries of
    /// <paramref name="heightData"/> for the block whose top-left pixel is
    /// (<paramref name="startX"/>, <paramref name="startY"/>).
    /// </summary>
    /// <remarks>
    /// The grid is one row and one column larger than the block so that the bottom and right
    /// edges have a neighbour to take a difference against.
    /// </remarks>
    internal static void InitGridData(
        ref BuildHeightsData heightData,
        TextureProjection projection,
        int resX,
        int resY,
        int startX,
        int startY,
        int sideW,
        int sideH
    )
    {
        if (projection == TextureProjection.Equirectangular)
            InitEquirectangular(ref heightData, resX, resY, startX, startY, sideW, sideH);
        else
            InitCubeFace(ref heightData, projection, resX, resY, startX, startY, sideW, sideH);

        heightData.vertHeight.Fill(heightData.sphere.radius);
        heightData.vertColor.Clear();
        heightData.allowScatter.Fill(true);
    }

    static void InitEquirectangular(
        ref BuildHeightsData heightData,
        int resX,
        int resY,
        int startX,
        int startY,
        int sideW,
        int sideH
    )
    {
        for (int r = 0; r < sideH; r++)
        {
            int globalY = Math.Min(startY + r, resY - 1);
            double lat = Math.PI / 2.0 - Math.PI * globalY / resY;
            double cosLat = Math.Cos(lat);
            double sinLat = Math.Sin(lat);

            for (int c = 0; c < sideW; c++)
            {
                int globalX = (startX + c) % resX;
                double lon = 2.0 * Math.PI * globalX / resX;
                var dir = new Vector3d(cosLat * Math.Sin(lon), sinLat, cosLat * Math.Cos(lon));

                SetVertex(ref heightData, r * sideW + c, dir, lat);
            }
        }
    }

    static void InitCubeFace(
        ref BuildHeightsData heightData,
        TextureProjection projection,
        int resX,
        int resY,
        int startX,
        int startY,
        int sideW,
        int sideH
    )
    {
        CubeAxes(projection, out var forward, out var right, out var up);

        for (int r = 0; r < sideH; r++)
        {
            // A face spans [-1, 1] on each axis. Unlike longitude these do not wrap, so the
            // extra row and column land on the edge shared with the neighbouring face.
            double v = 2.0 * (startY + r) / resY - 1.0;
            var rowDir = forward + v * up;

            for (int c = 0; c < sideW; c++)
            {
                double u = 2.0 * (startX + c) / resX - 1.0;
                var dir = (rowDir + u * right).Normalized();

                SetVertex(
                    ref heightData,
                    r * sideW + c,
                    dir,
                    Math.Asin(MathUtil.Clamp(dir.y, -1.0, 1.0))
                );
            }
        }
    }

    /// <summary>
    /// Resolves a cube face to the direction at its centre and the two axes that a pixel's
    /// coordinates, remapped to [-1, 1], step along.
    /// </summary>
    /// <remarks>
    /// This is the cube face layout shared by D3D and OpenGL, so the exported textures can be
    /// loaded as cubemap faces without reordering or flipping them.
    /// </remarks>
    static void CubeAxes(
        TextureProjection projection,
        out Vector3d forward,
        out Vector3d right,
        out Vector3d up
    )
    {
        switch (projection)
        {
            case TextureProjection.CubeXP:
                forward = new Vector3d(1.0, 0.0, 0.0);
                right = new Vector3d(0.0, 0.0, -1.0);
                up = new Vector3d(0.0, -1.0, 0.0);
                break;
            case TextureProjection.CubeXN:
                forward = new Vector3d(-1.0, 0.0, 0.0);
                right = new Vector3d(0.0, 0.0, 1.0);
                up = new Vector3d(0.0, -1.0, 0.0);
                break;
            case TextureProjection.CubeYP:
                forward = new Vector3d(0.0, 1.0, 0.0);
                right = new Vector3d(1.0, 0.0, 0.0);
                up = new Vector3d(0.0, 0.0, 1.0);
                break;
            case TextureProjection.CubeYN:
                forward = new Vector3d(0.0, -1.0, 0.0);
                right = new Vector3d(1.0, 0.0, 0.0);
                up = new Vector3d(0.0, 0.0, -1.0);
                break;
            case TextureProjection.CubeZP:
                forward = new Vector3d(0.0, 0.0, 1.0);
                right = new Vector3d(1.0, 0.0, 0.0);
                up = new Vector3d(0.0, -1.0, 0.0);
                break;
            default:
                forward = new Vector3d(0.0, 0.0, -1.0);
                right = new Vector3d(-1.0, 0.0, 0.0);
                up = new Vector3d(0.0, -1.0, 0.0);
                break;
        }
    }

    static void SetVertex(ref BuildHeightsData heightData, int i, Vector3d dir, double lat)
    {
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
