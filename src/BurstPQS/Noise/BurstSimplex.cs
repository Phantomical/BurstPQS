using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BurstPQS.Noise;

public unsafe struct BurstSimplex
{
    public readonly struct Guard : IDisposable
    {
        readonly ulong gcHandle;

        internal Guard(ulong gcHandle) => this.gcHandle = gcHandle;

        public readonly void Dispose() => UnsafeUtility.ReleaseGCObject(gcHandle);
    }

    public double octaves;

    public double persistence;

    public double frequency;

    [ReadOnly]
    int* perm;

    public static Guard Create(Simplex simplex, out BurstSimplex burst)
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

    static readonly int3[] grad3 =
    [
        new(1, 1, 0),
        new(-1, 1, 0),
        new(1, -1, 0),
        new(-1, -1, 0),
        new(1, 0, 1),
        new(-1, 0, 1),
        new(1, 0, -1),
        new(-1, 0, -1),
        new(0, 1, 1),
        new(0, -1, 1),
        new(0, 1, -1),
        new(0, -1, -1),
    ];

    // csharpier-ignore
    static readonly int[] p =
    [
        151, 160, 137, 91, 90, 15, 131, 13, 201, 95,
        96, 53, 194, 233, 7, 225, 140, 36, 103, 30,
        69, 142, 8, 99, 37, 240, 21, 10, 23, 190,
        6, 148, 247, 120, 234, 75, 0, 26, 197, 62,
        94, 252, 219, 203, 117, 35, 11, 32, 57, 177,
        33, 88, 237, 149, 56, 87, 174, 20, 125, 136,
        171, 168, 68, 175, 74, 165, 71, 134, 139, 48,
        27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
        60, 211, 133, 230, 220, 105, 92, 41, 55, 46,
        245, 40, 244, 102, 143, 54, 65, 25, 63, 161,
        1, 216, 80, 73, 209, 76, 132, 187, 208, 89,
        18, 169, 200, 196, 135, 130, 116, 188, 159, 86,
        164, 100, 109, 198, 173, 186, 3, 64, 52, 217,
        226, 250, 124, 123, 5, 202, 38, 147, 118, 126,
        255, 82, 85, 212, 207, 206, 59, 227, 47, 16,
        58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
        119, 248, 152, 2, 44, 154, 163, 70, 221, 153,
        101, 155, 167, 43, 172, 9, 129, 22, 39, 253,
        19, 98, 108, 110, 79, 113, 224, 232, 178, 185,
        112, 104, 218, 246, 97, 228, 251, 34, 242, 193,
        238, 210, 144, 12, 191, 179, 162, 241, 81, 51,
        145, 235, 249, 14, 239, 107, 49, 192, 214, 31,
        181, 199, 106, 157, 184, 84, 204, 176, 115, 121,
        50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
        222, 114, 67, 29, 24, 72, 243, 141, 128, 195,
        78, 66, 215, 61, 156, 180
    ];
}
