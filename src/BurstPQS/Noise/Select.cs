using LibNoise;
using UnityEngine;
using static BurstPQS.Noise.GradientNoiseBasis;
using static BurstPQS.Noise.NoiseUtil;

namespace BurstPQS.Noise;

public readonly struct Select<C, S1, S2>(
    LibNoise.Modifiers.Select noise,
    C control,
    S1 source1,
    S2 source2
) : IModule
    where C : IModule
    where S1 : IModule
    where S2 : IModule
{
    public readonly C ControlModule = control;
    public readonly S1 SourceModule1 = source1;
    public readonly S2 SourceModule2 = source2;
    public readonly double UpperBound = noise.UpperBound;
    public readonly double LowerBound = noise.LowerBound;
    public readonly double EdgeFalloff = noise.EdgeFalloff;

    public double GetValue(Vector3d coordinate)
    {
        return GetValue(coordinate.x, coordinate.y, coordinate.z);
    }

    public double GetValue(Vector3 coordinate)
    {
        return GetValue(coordinate.x, coordinate.y, coordinate.z);
    }

    public double GetValue(double x, double y, double z)
    {
        double value = ControlModule.GetValue(x, y, z);

        if (EdgeFalloff > 0.0)
        {
            if (value < LowerBound - EdgeFalloff)
                return SourceModule1.GetValue(x, y, z);
            if (value < LowerBound + EdgeFalloff)
            {
                double num = LowerBound - EdgeFalloff;
                double num2 = LowerBound + EdgeFalloff;
                double a = SCurve3((value - num) / (num2 - num));
                return LinearInterpolate(
                    SourceModule1.GetValue(x, y, z),
                    SourceModule2.GetValue(x, y, z),
                    a
                );
            }
            if (value < UpperBound - EdgeFalloff)
                return SourceModule2.GetValue(x, y, z);
            if (value < UpperBound + EdgeFalloff)
            {
                double num3 = UpperBound - EdgeFalloff;
                double num4 = UpperBound + EdgeFalloff;
                double a = SCurve3((value - num3) / (num4 - num3));
                return LinearInterpolate(
                    SourceModule2.GetValue(x, y, z),
                    SourceModule1.GetValue(x, y, z),
                    a
                );
            }
            return SourceModule1.GetValue(x, y, z);
        }
        else if (value >= LowerBound && value <= UpperBound)
            return SourceModule2.GetValue(x, y, z);
        else
            return SourceModule1.GetValue(x, y, z);
    }
}
