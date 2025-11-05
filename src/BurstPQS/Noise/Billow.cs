using LibNoise;
using UnityEngine;
using static BurstPQS.Noise.GradientNoiseBasis;

namespace BurstPQS.Noise;

public readonly struct Billow(LibNoise.Billow noise) : IModule
{
    public readonly double Frequency = noise.Frequency;
    public readonly double Persistence = noise.Persistence;
    public readonly NoiseQuality NoiseQuality = noise.NoiseQuality;
    public readonly int Seed = noise.Seed;
    public readonly double Lacunarity = noise.Lacunarity;
    public readonly int OctaveCount = noise.OctaveCount;

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
        double num = 0.0;
        double num2 = 0.0;
        double num3 = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            long num4 = (Seed + i) & 0xFFFFFFFFL;
            num2 = GradientCoherentNoise(x, y, z, (int)num4, NoiseQuality);
            num2 = 2.0 * System.Math.Abs(num2) - 1.0;
            num += num2 * num3;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            num3 *= Persistence;
        }
        return num + 0.5;
    }
}
