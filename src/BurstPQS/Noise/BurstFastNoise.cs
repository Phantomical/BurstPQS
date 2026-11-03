using BurstPQS.Collections;
using LibNoise;
using Unity.Mathematics;
using UnityEngine;
using static BurstPQS.Noise.NoiseUtil;

namespace BurstPQS.Noise;

/// <summary>
/// Burst-compatible version of <see cref="FastNoiseBasis"/>. The permutation
/// and gradient tables are generated from the seed, so they are copied out of
/// the source module.
/// </summary>
public unsafe struct BurstFastNoiseBasis
{
    fixed int permutations[512];
    fixed float gradients[512];

    public BurstFastNoiseBasis(FastNoiseBasis basis)
    {
        for (int i = 0; i < 512; ++i)
        {
            permutations[i] = basis.SelectedPermutations[i];
            gradients[i] = basis.GradientTable[i];
        }
    }

    // Unlike GradientNoiseBasis, this ignores the seed: it is already baked
    // into the tables.
    public readonly double GradientCoherentNoise(
        double x,
        double y,
        double z,
        NoiseQuality noiseQuality
    )
    {
        int x0 = (x > 0.0) ? (int)x : (int)x - 1;
        int y0 = (y > 0.0) ? (int)y : (int)y - 1;
        int z0 = (z > 0.0) ? (int)z : (int)z - 1;
        int xi = x0 & 0xFF;
        int yi = y0 & 0xFF;
        int zi = z0 & 0xFF;

        double a = 0.0;
        double a2 = 0.0;
        double a3 = 0.0;
        switch (noiseQuality)
        {
            case NoiseQuality.Low:
                a = x - x0;
                a2 = y - y0;
                a3 = z - z0;
                break;
            case NoiseQuality.Standard:
                a = SCurve3(x - x0);
                a2 = SCurve3(y - y0);
                a3 = SCurve3(z - z0);
                break;
            case NoiseQuality.High:
                a = SCurve5(x - x0);
                a2 = SCurve5(y - y0);
                a3 = SCurve5(z - z0);
                break;
        }

        int p7 = permutations[xi] + yi;
        int p8 = permutations[p7] + zi;
        int p9 = permutations[p7 + 1] + zi;
        int p10 = permutations[xi + 1] + yi;
        int p11 = permutations[p10] + zi;
        int p12 = permutations[p10 + 1] + zi;
        double n = LinearInterpolate(gradients[p8], gradients[p11], a);
        double n2 = LinearInterpolate(gradients[p9], gradients[p12], a);
        double n3 = LinearInterpolate(n, n2, a2);
        double n4 = LinearInterpolate(gradients[p8 + 1], gradients[p11 + 1], a);
        double n5 = LinearInterpolate(gradients[p9 + 1], gradients[p12 + 1], a);
        double n6 = LinearInterpolate(n4, n5, a2);
        return LinearInterpolate(n3, n6, a3);
    }

    public readonly double4 GradientCoherentNoise(
        double4 x,
        double4 y,
        double4 z,
        NoiseQuality noiseQuality
    )
    {
        int4 x0 = Floor(x);
        int4 y0 = Floor(y);
        int4 z0 = Floor(z);
        int4 xi = x0 & 0xFF;
        int4 yi = y0 & 0xFF;
        int4 zi = z0 & 0xFF;
        double4 a = Interpolant(x - x0, noiseQuality);
        double4 a2 = Interpolant(y - y0, noiseQuality);
        double4 a3 = Interpolant(z - z0, noiseQuality);

        int4 p7 = Permute(xi) + yi;
        int4 p8 = Permute(p7) + zi;
        int4 p9 = Permute(p7 + 1) + zi;
        int4 p10 = Permute(xi + 1) + yi;
        int4 p11 = Permute(p10) + zi;
        int4 p12 = Permute(p10 + 1) + zi;
        double4 n = LinearInterpolate(Gradient(p8), Gradient(p11), a);
        double4 n2 = LinearInterpolate(Gradient(p9), Gradient(p12), a);
        double4 n3 = LinearInterpolate(n, n2, a2);
        double4 n4 = LinearInterpolate(Gradient(p8 + 1), Gradient(p11 + 1), a);
        double4 n5 = LinearInterpolate(Gradient(p9 + 1), Gradient(p12 + 1), a);
        double4 n6 = LinearInterpolate(n4, n5, a2);
        return LinearInterpolate(n3, n6, a3);
    }

    readonly int4 Permute(int4 i) =>
        new(permutations[i.x], permutations[i.y], permutations[i.z], permutations[i.w]);

    readonly double4 Gradient(int4 i) =>
        new(gradients[i.x], gradients[i.y], gradients[i.z], gradients[i.w]);
}

/// <summary>
/// Burst-compatible version of <see cref="LibNoise.FastNoise"/>.
/// </summary>
public unsafe struct BurstFastNoise(FastNoise noise) : IModule, IBatchModule
{
    public double Frequency = noise.Frequency;
    public double Persistence = noise.Persistence;
    public double Lacunarity = noise.Lacunarity;
    public int OctaveCount = noise.OctaveCount;
    public NoiseQuality NoiseQuality = noise.NoiseQuality;
    BurstFastNoiseBasis basis = new(noise);

    public readonly double GetValue(Vector3d coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public readonly double GetValue(Vector3 coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public readonly double GetValue(double x, double y, double z)
    {
        double value = 0.0;
        double amplitude = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            value += basis.GradientCoherentNoise(x, y, z, NoiseQuality) * amplitude;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            amplitude *= Persistence;
        }
        return value;
    }

    public readonly double4 GetValue(double4 x, double4 y, double4 z)
    {
        double4 value = 0.0;
        double amplitude = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            value += basis.GradientCoherentNoise(x, y, z, NoiseQuality) * amplitude;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            amplitude *= Persistence;
        }
        return value;
    }

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count)
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

/// <summary>
/// Burst-compatible version of <see cref="LibNoise.FastBillow"/>.
/// </summary>
public unsafe struct BurstFastBillow(FastBillow noise) : IModule, IBatchModule
{
    public double Frequency = noise.Frequency;
    public double Persistence = noise.Persistence;
    public double Lacunarity = noise.Lacunarity;
    public int OctaveCount = noise.OctaveCount;
    public NoiseQuality NoiseQuality = noise.NoiseQuality;
    BurstFastNoiseBasis basis = new(noise);

    public readonly double GetValue(Vector3d coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public readonly double GetValue(Vector3 coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public readonly double GetValue(double x, double y, double z)
    {
        double value = 0.0;
        double amplitude = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            double signal = basis.GradientCoherentNoise(x, y, z, NoiseQuality);
            signal = 2.0 * System.Math.Abs(signal) - 1.0;
            value += signal * amplitude;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            amplitude *= Persistence;
        }
        return value + 0.5;
    }

    public readonly double4 GetValue(double4 x, double4 y, double4 z)
    {
        double4 value = 0.0;
        double amplitude = 1.0;
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        for (int i = 0; i < OctaveCount; i++)
        {
            double4 signal = basis.GradientCoherentNoise(x, y, z, NoiseQuality);
            signal = 2.0 * math.abs(signal) - 1.0;
            value += signal * amplitude;
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
            amplitude *= Persistence;
        }
        return value + 0.5;
    }

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count)
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

/// <summary>
/// Burst-compatible version of <see cref="LibNoise.FastRidgedMultifractal"/>.
/// </summary>
public unsafe struct BurstFastRidgedMultifractal(FastRidgedMultifractal noise)
    : IModule,
        IBatchModule
{
    public FixedArray30<double> SpectralWeights = new(noise.SpectralWeights);
    public double Frequency = noise.Frequency;
    public double Lacunarity = noise.Lacunarity;
    public int OctaveCount = noise.OctaveCount;
    public NoiseQuality NoiseQuality = noise.NoiseQuality;
    BurstFastNoiseBasis basis = new(noise);

    public readonly double GetValue(Vector3d coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public readonly double GetValue(Vector3 coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public readonly double GetValue(double x, double y, double z)
    {
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        double value = 0.0;
        double weight = 1.0;
        for (int i = 0; i < OctaveCount; i++)
        {
            double signal = basis.GradientCoherentNoise(x, y, z, NoiseQuality);
            signal = System.Math.Abs(signal);
            signal = 1.0 - signal;
            signal *= signal;
            signal *= weight;
            weight = signal * 2.0;
            if (weight > 1.0)
                weight = 1.0;
            if (weight < 0.0)
                weight = 0.0;
            value += signal * SpectralWeights[i];
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
        }
        return value * 1.25 - 1.0;
    }

    public readonly double4 GetValue(double4 x, double4 y, double4 z)
    {
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        double4 value = 0.0;
        double4 weight = 1.0;
        for (int i = 0; i < OctaveCount; i++)
        {
            double4 signal = basis.GradientCoherentNoise(x, y, z, NoiseQuality);
            signal = 1.0 - math.abs(signal);
            signal *= signal;
            signal *= weight;
            weight = signal * 2.0;
            weight = math.select(weight, 1.0, weight > 1.0);
            weight = math.select(weight, 0.0, weight < 0.0);
            value += signal * SpectralWeights[i];
            x *= Lacunarity;
            y *= Lacunarity;
            z *= Lacunarity;
        }
        return value * 1.25 - 1.0;
    }

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count)
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
