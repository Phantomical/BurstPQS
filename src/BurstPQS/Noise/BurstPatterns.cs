using LibNoise;
using Unity.Mathematics;
using UnityEngine;
using static BurstPQS.Noise.NoiseUtil;

namespace BurstPQS.Noise;

/// <summary>
/// Burst-compatible version of <see cref="LibNoise.Checkerboard"/>.
/// </summary>
public readonly unsafe struct BurstCheckerboard : IModule, IBatchModule, IVectorModule
{
    public double GetValue(Vector3d coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public double GetValue(Vector3 coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public double GetValue(double x, double y, double z) =>
        GetValue(new double4(x), new double4(y), new double4(z)).x;

    public double4 GetValue(double4 x, double4 y, double4 z)
    {
        int4 parity = (Floor(x) ^ Floor(y) ^ Floor(z)) & 1;
        return math.select(1.0, -1.0, parity != 0);
    }

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count) =>
        BatchUtil.GetValues(in this, x, y, z, output, count);

    public readonly void GetValues(Vector3d* coords, double* output, int count) =>
        BatchUtil.GetValues(in this, coords, output, count);
}

/// <summary>
/// Burst-compatible version of <see cref="LibNoise.Cylinders"/>.
/// </summary>
public readonly unsafe struct BurstCylinders(Cylinders noise) : IModule, IBatchModule, IVectorModule
{
    public readonly double Frequency = noise.Frequency;

    public double GetValue(Vector3d coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public double GetValue(Vector3 coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public double GetValue(double x, double y, double z) =>
        GetValue(new double4(x), new double4(y), new double4(z)).x;

    public double4 GetValue(double4 x, double4 y, double4 z)
    {
        x *= Frequency;
        z *= Frequency;
        double4 dist = math.sqrt(x * x + z * z);
        double4 frac = dist - Floor(dist);
        double4 rem = 1.0 - frac;
        return 1.0 - math.select(rem, frac, frac < rem) * 4.0;
    }

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count) =>
        BatchUtil.GetValues(in this, x, y, z, output, count);

    public readonly void GetValues(Vector3d* coords, double* output, int count) =>
        BatchUtil.GetValues(in this, coords, output, count);
}

/// <summary>
/// Burst-compatible version of <see cref="LibNoise.Spheres"/>.
/// </summary>
public readonly unsafe struct BurstSpheres(Spheres noise) : IModule, IBatchModule, IVectorModule
{
    public readonly double Frequency = noise.Frequency;

    public double GetValue(Vector3d coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public double GetValue(Vector3 coordinate) =>
        GetValue(coordinate.x, coordinate.y, coordinate.z);

    public double GetValue(double x, double y, double z) =>
        GetValue(new double4(x), new double4(y), new double4(z)).x;

    // LibNoise floors x rather than the distance here, so we do the same.
    public double4 GetValue(double4 x, double4 y, double4 z)
    {
        x *= Frequency;
        y *= Frequency;
        z *= Frequency;
        double4 dist = math.sqrt(x * x + y * y + z * z);
        double4 frac = dist - Floor(x);
        double4 rem = 1.0 - frac;
        return 1.0 - math.select(rem, frac, frac < rem) * 4.0;
    }

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count) =>
        BatchUtil.GetValues(in this, x, y, z, output, count);

    public readonly void GetValues(Vector3d* coords, double* output, int count) =>
        BatchUtil.GetValues(in this, coords, output, count);
}
