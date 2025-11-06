using System;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using KSPSimplex = Simplex;

namespace BurstPQS.Noise;

public unsafe struct BurstSimplex
{
    public readonly struct Guard : IDisposable
    {
        readonly ulong gcHandle;

        internal Guard(ulong gcHandle) => this.gcHandle = gcHandle;

        public readonly void Dispose() => UnsafeUtility.ReleaseGCObject(gcHandle);
    }

    static readonly int3[] grad3 =
    [
        .. KSPSimplex.grad3.Select(grad => new int3(grad[0], grad[1], grad[2])),
    ];
    static readonly int[] p = [.. KSPSimplex.p];

    public double octaves;

    public double persistence;

    public double frequency;
    int* perm;

    public static Guard Create(KSPSimplex simplex, out BurstSimplex burst)
    {
        if (simplex.perm.Length != 0x100)
            throw new ArgumentException("simplex perm array was not the correct length");

        var perm = UnsafeUtility.PinGCArrayAndGetDataAddress(simplex.perm, out var gcHandle);

        burst = new()
        {
            octaves = simplex.octaves,
            persistence = simplex.persistence,
            frequency = simplex.frequency,
            perm = (int*)perm,
        };

        return new Guard(gcHandle);
    }

    private static int fastfloor(double x)
    {
        if (!(x > 0.0))
        {
            return (int)x - 1;
        }
        return (int)x;
    }

    private static double dot(int3 g, double x, double y, double z)
    {
        return math.dot((double3)g, new(x, y, z));
    }

    private readonly double value(double xin, double yin, double zin)
    {
        double F3 = 1.0 / 3.0;
        double s = (xin + yin + zin) * F3;
        int i = fastfloor(xin + s);
        int j = fastfloor(yin + s);
        int k = fastfloor(zin + s);
        double G3 = 1.0 / 6.0;
        double t = (double)(i + j + k) * G3;
        double X0 = (double)i - t;
        double Y0 = (double)j - t;
        double Z0 = (double)k - t;
        double x0 = xin - X0;
        double y0 = yin - Y0;
        double z0 = zin - Z0;
        int i1,
            j1,
            k1,
            i2,
            j2,
            k2;

        if (x0 >= y0)
        {
            if (y0 >= z0)
            {
                i1 = 1;
                j1 = 0;
                k1 = 0;
                i2 = 1;
                j2 = 1;
                k2 = 0;
            }
            else if (x0 >= z0)
            {
                i1 = 1;
                j1 = 0;
                k1 = 0;
                i2 = 1;
                j2 = 0;
                k2 = 1;
            }
            else
            {
                i1 = 0;
                j1 = 0;
                k1 = 1;
                i2 = 1;
                j2 = 0;
                k2 = 1;
            }
        }
        else if (y0 < z0)
        {
            i1 = 0;
            j1 = 0;
            k1 = 1;
            i2 = 0;
            j2 = 1;
            k2 = 1;
        }
        else if (x0 < z0)
        {
            i1 = 0;
            j1 = 1;
            k1 = 0;
            i2 = 0;
            j2 = 1;
            k2 = 1;
        }
        else
        {
            i1 = 0;
            j1 = 1;
            k1 = 0;
            i2 = 1;
            j2 = 1;
            k2 = 0;
        }
        double x1 = x0 - (double)i1 + G3;
        double y1 = y0 - (double)j1 + G3;
        double z1 = z0 - (double)k1 + G3;
        double x2 = x0 - (double)i2 + 2.0 * G3;
        double y2 = y0 - (double)j2 + 2.0 * G3;
        double z2 = z0 - (double)k2 + 2.0 * G3;
        double x3 = x0 - 1.0 + 3.0 * G3;
        double y3 = y0 - 1.0 + 3.0 * G3;
        double z3 = z0 - 1.0 + 3.0 * G3;
        int ii = i & 0xFF;
        int jj = j & 0xFF;
        int kk = k & 0xFF;
        int gi0 = perm[ii + perm[jj + perm[kk]]] % 12;
        int gi1 = perm[ii + i1 + perm[jj + j1 + perm[kk + k1]]] % 12;
        int gi2 = perm[ii + i2 + perm[jj + j2 + perm[kk + k2]]] % 12;
        int gi3 = perm[ii + 1 + perm[jj + 1 + perm[kk + 1]]] % 12;
        double t0 = 0.6 - x0 * x0 - y0 * y0 - z0 * z0;

        double n0,
            n1,
            n2,
            n3;
        if (t0 < 0.0)
        {
            n0 = 0.0;
        }
        else
        {
            t0 *= t0;
            n0 = t0 * t0 * dot(grad3[gi0], x0, y0, z0);
        }
        double t1 = 0.6 - x1 * x1 - y1 * y1 - z1 * z1;
        if (t1 < 0.0)
        {
            n1 = 0.0;
        }
        else
        {
            t1 *= t1;
            n1 = t1 * t1 * dot(grad3[gi1], x1, y1, z1);
        }
        double t2 = 0.6 - x2 * x2 - y2 * y2 - z2 * z2;
        if (t2 < 0.0)
        {
            n2 = 0.0;
        }
        else
        {
            t2 *= t2;
            n2 = t2 * t2 * dot(grad3[gi2], x2, y2, z2);
        }
        double t3 = 0.6 - x3 * x3 - y3 * y3 - z3 * z3;
        if (t3 < 0.0)
        {
            n3 = 0.0;
        }
        else
        {
            t3 *= t3;
            n3 = t3 * t3 * dot(grad3[gi3], x3, y3, z3);
        }
        return 32.0 * (n0 + n1 + n2 + n3);
    }

    public readonly double noiseNormalized(Vector3d v3d)
    {
        return (noise(v3d.x, v3d.y, v3d.z) + 1.0) * 0.5;
    }

    public readonly double noise(Vector3d v3d)
    {
        return noise(v3d.x, v3d.y, v3d.z);
    }

    public readonly double noiseNormalized(double x, double y, double z)
    {
        return (noise(x, y, z) + 1.0) * 0.5;
    }

    public readonly double noise(double x, double y, double z)
    {
        double total = 0.0;
        double amplitude = 1.0;
        double f = frequency;
        double maxAmplitude = 0.0;
        for (double itr = 0.0; itr < octaves; itr += 1.0)
        {
            total += value(x * f, y * f, z * f) * amplitude;
            f *= 2.0;
            maxAmplitude += amplitude;
            amplitude *= persistence;
        }
        return total / maxAmplitude;
    }
}
