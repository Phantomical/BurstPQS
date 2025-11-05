using LibNoise;
using static BurstPQS.Noise.NoiseUtil;

namespace BurstPQS.Noise;

public static class GradientNoiseBasis
{
    static readonly double[] RandomVectors = [.. LibNoise.GradientNoiseBasis.RandomVectors];

    public static double GradientCoherentNoise(
        double x,
        double y,
        double z,
        int seed,
        NoiseQuality noiseQuality
    )
    {
        int num = ((x > 0.0) ? ((int)x) : ((int)x - 1));
        int ix = num + 1;
        int num2 = ((y > 0.0) ? ((int)y) : ((int)y - 1));
        int iy = num2 + 1;
        int num3 = ((z > 0.0) ? ((int)z) : ((int)z - 1));
        int iz = num3 + 1;
        double a = 0.0;
        double a2 = 0.0;
        double a3 = 0.0;
        switch (noiseQuality)
        {
            case NoiseQuality.Low:
                a = x - (double)num;
                a2 = y - (double)num2;
                a3 = z - (double)num3;
                break;
            case NoiseQuality.Standard:
                a = SCurve3(x - (double)num);
                a2 = SCurve3(y - (double)num2);
                a3 = SCurve3(z - (double)num3);
                break;
            case NoiseQuality.High:
                a = SCurve5(x - (double)num);
                a2 = SCurve5(y - (double)num2);
                a3 = SCurve5(z - (double)num3);
                break;
        }
        double n = GradientNoise(x, y, z, num, num2, num3, seed);
        double n2 = GradientNoise(x, y, z, ix, num2, num3, seed);
        double n3 = LinearInterpolate(n, n2, a);
        n = GradientNoise(x, y, z, num, iy, num3, seed);
        n2 = GradientNoise(x, y, z, ix, iy, num3, seed);
        double n4 = LinearInterpolate(n, n2, a);
        double n5 = LinearInterpolate(n3, n4, a2);
        n = GradientNoise(x, y, z, num, num2, iz, seed);
        n2 = GradientNoise(x, y, z, ix, num2, iz, seed);
        n3 = LinearInterpolate(n, n2, a);
        n = GradientNoise(x, y, z, num, iy, iz, seed);
        n2 = GradientNoise(x, y, z, ix, iy, iz, seed);
        n4 = LinearInterpolate(n, n2, a);
        double n6 = LinearInterpolate(n3, n4, a2);
        return LinearInterpolate(n5, n6, a3);
    }

    static double GradientNoise(double fx, double fy, double fz, int ix, int iy, int iz, long seed)
    {
        long num = (1619 * ix + 31337 * iy + 6971 * iz + 1013L * seed) & 0xFFFFFFFFL;
        num ^= num >> 8;
        num &= 0xFFL;
        double num2 = RandomVectors[num << 2];
        double num3 = RandomVectors[(num << 2) + 1L];
        double num4 = RandomVectors[(num << 2) + 2L];
        double num5 = fx - (double)ix;
        double num6 = fy - (double)iy;
        double num7 = fz - (double)iz;
        return (num2 * num5 + num3 * num6 + num4 * num7) * 2.12;
    }
}
