using LibNoise;
using Unity.Mathematics;
using UnityEngine;
using static BurstPQS.Noise.GradientNoiseBasis;

namespace BurstPQS.Noise;

/// <summary>
/// Burst-compatible perlin noise.
/// </summary>
/// <param name="perlin"></param>
public readonly unsafe struct BurstPerlin(LibNoise.Perlin perlin)
    : IModule,
        IBatchModule,
        IVectorModule
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

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count) =>
        BatchUtil.GetValues(in this, x, y, z, output, count);

    public readonly void GetValues(Vector3d* coords, double* output, int count) =>
        BatchUtil.GetValues(in this, coords, output, count);
}
