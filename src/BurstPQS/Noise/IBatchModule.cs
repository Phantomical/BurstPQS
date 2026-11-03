using Unity.Mathematics;

namespace BurstPQS.Noise;

/// <summary>
/// A noise module that can evaluate a whole batch of points at once.
/// </summary>
public unsafe interface IBatchModule
{
    /// <summary>
    /// Writes the noise value at <c>(x[i], y[i], z[i])</c> to
    /// <c>output[i]</c> for each <c>i</c> in <c>[0, count)</c>.
    /// </summary>
    void GetValues(double* x, double* y, double* z, double* output, int count);
}

// Lets batch implementations process 4 points at a time, padding the final
// partial group with zeros.
internal static unsafe class BatchUtil
{
    public static double4 Load(double* p, int i, int count)
    {
        if (i + 4 <= count)
            return *(double4*)(p + i);

        double4 v = 0.0;
        for (int j = 0; i + j < count; ++j)
            v[j] = p[i + j];
        return v;
    }

    public static void Store(double* p, int i, int count, double4 v)
    {
        if (i + 4 <= count)
        {
            *(double4*)(p + i) = v;
            return;
        }

        for (int j = 0; i + j < count; ++j)
            p[i + j] = v[j];
    }
}
