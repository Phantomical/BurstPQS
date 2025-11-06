using System;

namespace BurstPQS.Util;

public static class MathUtil
{
    public static double CubicHermite(
        double start,
        double end,
        double startTangent,
        double endTangent,
        double t
    )
    {
        double ct2 = t * t;
        double ct3 = ct2 * t;
        return start * (2.0 * ct3 - 3.0 * ct2 + 1.0)
            + startTangent * (ct3 - 2.0 * ct2 + t)
            + end * (-2.0 * ct3 + 3.0 * ct2)
            + endTangent * (ct3 - ct2);
    }

    public static double Lerp(double v2, double v1, double dt)
    {
        return v1 * dt + v2 * (1.0 - dt);
    }

    public static double Clamp(double v, double min, double max) => Math.Min(Math.Max(v, min), max);

    public static double Clamp01(double v) => Clamp(v, 0.0, 1.0);
}
