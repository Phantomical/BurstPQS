using LibNoise;
using UnityEngine;
using static BurstPQS.Noise.ValueNoiseBasis;

namespace BurstPQS.Noise;

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
        int num = ((x > 0.0) ? ((int)x) : ((int)x - 1));
        int num2 = ((y > 0.0) ? ((int)y) : ((int)y - 1));
        int num3 = ((z > 0.0) ? ((int)z) : ((int)z - 1));
        double num4 = 2147483647.0;
        double num5 = 0.0;
        double num6 = 0.0;
        double num7 = 0.0;
        for (int i = num3 - 2; i <= num3 + 2; i++)
        {
            for (int j = num2 - 2; j <= num2 + 2; j++)
            {
                for (int k = num - 2; k <= num + 2; k++)
                {
                    double num8 = (double)k + ValueNoise(k, j, i, Seed);
                    double num9 = (double)j + ValueNoise(k, j, i, Seed + 1);
                    double num10 = (double)i + ValueNoise(k, j, i, Seed + 2);
                    double num11 = num8 - x;
                    double num12 = num9 - y;
                    double num13 = num10 - z;
                    double num14 = num11 * num11 + num12 * num12 + num13 * num13;
                    if (num14 < num4)
                    {
                        num4 = num14;
                        num5 = num8;
                        num6 = num9;
                        num7 = num10;
                    }
                }
            }
        }
        double num18;
        if (DistanceEnabled)
        {
            double num15 = num5 - x;
            double num16 = num6 - y;
            double num17 = num7 - z;
            num18 =
                System.Math.Sqrt(num15 * num15 + num16 * num16 + num17 * num17) * Math.Sqrt3 - 1.0;
        }
        else
        {
            num18 = 0.0;
        }
        int x2 = ((num5 > 0.0) ? ((int)num5) : ((int)num5 - 1));
        int y2 = ((num6 > 0.0) ? ((int)num6) : ((int)num6 - 1));
        int z2 = ((num7 > 0.0) ? ((int)num7) : ((int)num7 - 1));
        return num18 + Displacement * ValueNoise(x2, y2, z2);
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
        int num = ((x > 0.0) ? ((int)x) : ((int)x - 1));
        int num2 = ((y > 0.0) ? ((int)y) : ((int)y - 1));
        int num3 = ((z > 0.0) ? ((int)z) : ((int)z - 1));
        double num4 = 2147483647.0;
        double x2 = 0.0;
        double y2 = 0.0;
        double z2 = 0.0;
        for (int i = num3 - 2; i <= num3 + 2; i++)
        {
            for (int j = num2 - 2; j <= num2 + 2; j++)
            {
                for (int k = num - 2; k <= num + 2; k++)
                {
                    double num5 = (double)k + ValueNoise(k, j, i, Seed);
                    double num6 = (double)j + ValueNoise(k, j, i, Seed + 1);
                    double num7 = (double)i + ValueNoise(k, j, i, Seed + 2);
                    double num8 = num5 - x;
                    double num9 = num6 - y;
                    double num10 = num7 - z;
                    double num11 = num8 * num8 + num9 * num9 + num10 * num10;
                    if (num11 < num4)
                    {
                        num4 = num11;
                        x2 = num5;
                        y2 = num6;
                        z2 = num7;
                    }
                }
            }
        }
        return new Vector3d(x2, y2, z2);
    }
}
