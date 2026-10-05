using LibNoise;
using UnityEngine;
using static BurstPQS.Noise.ValueNoiseBasis;

namespace BurstPQS.Noise;

/// <summary>
/// Burst-compatible voronoi noise.
/// </summary>
public readonly struct BurstVoronoi : IModule
{
    public readonly int Seed;
    public readonly double Displacement;
    public readonly double Frequency;
    public readonly bool DistanceEnabled;

    public BurstVoronoi(Voronoi voronoi)
    {
        Seed = voronoi.Seed;
        Displacement = voronoi.Displacement;
        Frequency = voronoi.Frequency;
        DistanceEnabled = voronoi.DistanceEnabled;
    }

    public double GetValue(Vector3d coordinate)
    {
        return GetValue(coordinate.x, coordinate.y, coordinate.z);
    }

    public double GetValue(Vector3 coordinate)
    {
        return GetValue(coordinate.x, coordinate.y, coordinate.z);
    }

    public double GetValue(double x, double y, double z)
    {
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        int cellX = ((x > 0.0) ? ((int)x) : ((int)x - 1));
        int cellY = ((y > 0.0) ? ((int)y) : ((int)y - 1));
        int cellZ = ((z > 0.0) ? ((int)z) : ((int)z - 1));
        double minDist = 2147483647.0;
        double nearestX = 0.0;
        double nearestY = 0.0;
        double nearestZ = 0.0;
        for (int i = cellZ - 2; i <= cellZ + 2; i++)
        {
            for (int j = cellY - 2; j <= cellY + 2; j++)
            {
                for (int k = cellX - 2; k <= cellX + 2; k++)
                {
                    double pointX = (double)k + ValueNoise(k, j, i, Seed);
                    double pointY = (double)j + ValueNoise(k, j, i, Seed + 1);
                    double pointZ = (double)i + ValueNoise(k, j, i, Seed + 2);
                    double dx = pointX - x;
                    double dy = pointY - y;
                    double dz = pointZ - z;
                    double dist = dx * dx + dy * dy + dz * dz;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestX = pointX;
                        nearestY = pointY;
                        nearestZ = pointZ;
                    }
                }
            }
        }
        double value;
        if (DistanceEnabled)
        {
            double dx = nearestX - x;
            double dy = nearestY - y;
            double dz = nearestZ - z;
            value =
                System.Math.Sqrt(dx * dx + dy * dy + dz * dz) * Math.Sqrt3 - 1.0;
        }
        else
        {
            value = 0.0;
        }
        int nearestCellX = ((nearestX > 0.0) ? ((int)nearestX) : ((int)nearestX - 1));
        int nearestCellY = ((nearestY > 0.0) ? ((int)nearestY) : ((int)nearestY - 1));
        int nearestCellZ = ((nearestZ > 0.0) ? ((int)nearestZ) : ((int)nearestZ - 1));
        return value + Displacement * ValueNoise(nearestCellX, nearestCellY, nearestCellZ);
    }

    public Vector3d GetNearest(Vector3d candidate)
    {
        return GetNearest(candidate.x, candidate.y, candidate.z);
    }

    public Vector3d GetNearest(double x, double y, double z)
    {
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        int cellX = ((x > 0.0) ? ((int)x) : ((int)x - 1));
        int cellY = ((y > 0.0) ? ((int)y) : ((int)y - 1));
        int cellZ = ((z > 0.0) ? ((int)z) : ((int)z - 1));
        double minDist = 2147483647.0;
        double nearestX = 0.0;
        double nearestY = 0.0;
        double nearestZ = 0.0;
        for (int i = cellZ - 2; i <= cellZ + 2; i++)
        {
            for (int j = cellY - 2; j <= cellY + 2; j++)
            {
                for (int k = cellX - 2; k <= cellX + 2; k++)
                {
                    double pointX = (double)k + ValueNoise(k, j, i, Seed);
                    double pointY = (double)j + ValueNoise(k, j, i, Seed + 1);
                    double pointZ = (double)i + ValueNoise(k, j, i, Seed + 2);
                    double dx = pointX - x;
                    double dy = pointY - y;
                    double dz = pointZ - z;
                    double dist = dx * dx + dy * dy + dz * dz;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestX = pointX;
                        nearestY = pointY;
                        nearestZ = pointZ;
                    }
                }
            }
        }
        return new Vector3d(nearestX, nearestY, nearestZ);
    }
}
