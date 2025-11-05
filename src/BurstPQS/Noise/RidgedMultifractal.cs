using BurstPQS.Collections;
using LibNoise;
using UnityEngine;
using static BurstPQS.Noise.GradientNoiseBasis;

namespace BurstPQS.Noise;

public readonly struct RidgedMultifractal(LibNoise.RidgedMultifractal noise) : IModule
{
    public readonly FixedArray30<double> SpectralWeights = new(noise.SpectralWeights);
    public readonly double Frequency = noise.Frequency;
    public readonly NoiseQuality NoiseQuality = noise.NoiseQuality;
    public readonly int Seed = noise.Seed;
    public readonly double Lacunarity = noise.Lacunarity;
    public readonly double OctaveCount = noise.OctaveCount;

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
        double num = 0.0;
        double num2 = 0.0;
        double num3 = 1.0;
        double num4 = 1.0;
        double num5 = 2.0;
        for (int i = 0; i < OctaveCount; i++)
        {
            long num6 = (Seed + i) & 0x7FFFFFFF;
            num = GradientCoherentNoise(x, y, z, (int)num6, NoiseQuality);
            num = System.Math.Abs(num);
            num = num4 - num;
            num *= num;
            num *= num3;
            num3 = num * num5;
            if (num3 > 1.0)
            {
                num3 = 1.0;
            }
            if (num3 < 0.0)
            {
                num3 = 0.0;
            }
            num2 += num * SpectralWeights[i];
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
        }
        return num2 * 1.25 - 1.0;
    }
}
