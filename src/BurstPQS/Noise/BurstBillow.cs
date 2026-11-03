using LibNoise;
using Unity.Mathematics;
using UnityEngine;
using static BurstPQS.Noise.GradientNoiseBasis;

namespace BurstPQS.Noise;

/// <summary>
/// Burst-compatible billow noise.
/// PQS jobs.
/// </summary>
/// <param name="noise"></param>
public readonly unsafe struct BurstBillow(LibNoise.Billow noise) : IModule, IBatchModule
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

    public double4 GetValue(double4 x, double4 y, double4 z)
    {
        double4 value = 0.0;
        double amplitude = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            double4 signal = GradientCoherentNoise(x, y, z, Seed + i, NoiseQuality);
            signal = 2.0 * math.abs(signal) - 1.0;
            value += signal * amplitude;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            amplitude *= Persistence;
        }
        return value + 0.5;
    }

    public void GetValues(double* x, double* y, double* z, double* output, int count)
    {
        for (int i = 0; i < count; i += 4)
        {
            var value = GetValue(
                BatchUtil.Load(x, i, count),
                BatchUtil.Load(y, i, count),
                BatchUtil.Load(z, i, count)
            );
            BatchUtil.Store(output, i, count, value);
        }
    }
}
