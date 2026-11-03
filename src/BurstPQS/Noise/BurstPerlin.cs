using LibNoise;
using Unity.Mathematics;
using UnityEngine;
using static BurstPQS.Noise.GradientNoiseBasis;

namespace BurstPQS.Noise;

/// <summary>
/// Burst-compatible perlin noise.
/// </summary>
/// <param name="perlin"></param>
public readonly unsafe struct BurstPerlin(LibNoise.Perlin perlin) : IModule, IBatchModule
{
    public readonly double Frequency = perlin.Frequency;
    public readonly double Persistence = perlin.Persistence;
    public readonly NoiseQuality NoiseQuality = perlin.NoiseQuality;
    public readonly int Seed = perlin.Seed;
    public readonly double Lacunarity = perlin.Lacunarity;
    public readonly int OctaveCount = perlin.OctaveCount;

    public double GetValue(Vector3d coords) => GetValue(coords.x, coords.y, coords.z);

    public double GetValue(Vector3 coords) => GetValue((Vector3d)coords);

    public double GetValue(double x, double y, double z)
    {
        double num = 0.0;
        double num2;
        double num3 = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            long num4 = (Seed + i) & 0xFFFFFFFFL;
            num2 = GradientCoherentNoise(x, y, z, (int)num4, NoiseQuality);
            num += num2 * num3;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            num3 *= Persistence;
        }
        return num;
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
            value += GradientCoherentNoise(x, y, z, Seed + i, NoiseQuality) * amplitude;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            amplitude *= Persistence;
        }
        return value;
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
