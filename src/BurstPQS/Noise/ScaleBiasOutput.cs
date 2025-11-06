using System;
using LibNoise;
using UnityEngine;

namespace BurstPQS.Noise;

public readonly struct ScaleBiasOutput<N> : IModule
    where N : IModule
{
    public readonly double Scale;
    public readonly double Bias;
    public readonly N SourceModule;

    public ScaleBiasOutput(LibNoise.Modifiers.ScaleBiasOutput scale, N sourceModule)
    {
        if (sourceModule is null)
            throw new ArgumentNullException("A source module must be provided.");

        SourceModule = sourceModule;
        Bias = scale.Bias;
        Scale = scale.Scale;
    }

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
        if (SourceModule is null)
            throw new Exception("A source module must be provided.");

        return SourceModule.GetValue(x, y, z) * Scale + Bias;
    }
}
