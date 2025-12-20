using LibNoise;
using UnityEngine;
using static BurstPQS.Noise.GradientNoiseBasis;

namespace BurstPQS.Noise;

public readonly struct BurstPerlin(LibNoise.Perlin perlin) : IModule
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
}
