using System;
using Contracts.Parameters;
using Unity.Mathematics;

namespace BurstPQS.Noise;

public static class NoiseUtil
{
    public const double Sqrt2 = 1.4142135623730951;
    public const double Sqrt3 = 1.7320508075688772;
    public const double Deg2Rad = Math.PI / 180.0;

    public static double CubicInterpolate(double n0, double n1, double n2, double n3, double a)
    {
        double num = n3 - n2 - (n0 - n1);
        double num2 = n0 - n1 - num;
        double num3 = n2 - n0;
        return num * a * a * a + num2 * a * a + num3 * a + n1;
    }

    public static double GetSmaller(double a, double b) => Math.Min(a, b);

    public static double GetLarger(double a, double b) => Math.Max(a, b);

    public static void SwapValues(ref double a, ref double b) => (b, a) = (a, b);

    public static double LinearInterpolate(double n0, double n1, double a) =>
        (1.0 - a) * n0 + a * n1;

    public static double SCurve3(double a)
    {
        return a * a * (3.0 - 2.0 * a);
    }

    public static double SCurve5(double a)
    {
        double a3 = a * a * a;
        double a4 = a3 * a;
        double a5 = a4 * a;
        return 6.0 * a5 - 15.0 * a4 + 10.0 * a3;
    }

    public static void LatLonToXYZ(double lat, double lon, ref double x, ref double y, ref double z)
    {
        math.sincos(lat * Deg2Rad, out var sinLat, out var cosLat);
        math.sincos(lon * Deg2Rad, out var sinLon, out var cosLon);

        x = cosLat * cosLon;
        y = sinLat;
        z = cosLat * sinLon;
    }
}
