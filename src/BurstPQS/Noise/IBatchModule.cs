using System.Runtime.CompilerServices;
using BurstPQS.Collections;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86.Avx;

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

    /// <summary>
    /// Writes the noise value at <c>coords[i]</c> to <c>output[i]</c> for
    /// each <c>i</c> in <c>[0, count)</c>.
    /// </summary>
    void GetValues(Vector3d* coords, double* output, int count);
}

/// <summary>
/// A noise module that can evaluate 4 points at once, one per lane.
/// </summary>
public interface IVectorModule
{
    double4 GetValue(double4 x, double4 y, double4 z);
}

// Implements IBatchModule for IVectorModules, 4 points at a time. The final
// partial group is padded with zeros.
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

    // Loads coords[i..i + 4) and transposes them into one vector per axis.
    public static void Load(
        Vector3d* coords,
        int i,
        int count,
        out double4 x,
        out double4 y,
        out double4 z
    )
    {
        if (i + 4 <= count)
        {
            Transpose((double*)(coords + i), out x, out y, out z);
            return;
        }

        x = 0.0;
        y = 0.0;
        z = 0.0;
        for (int j = 0; i + j < count; ++j)
        {
            x[j] = coords[i + j].x;
            y[j] = coords[i + j].y;
            z[j] = coords[i + j].z;
        }
    }

    // p points at 4 (x, y, z) triples. As 3 vectors they are
    //   m0 = [x0 y0 | z0 x1], m1 = [y1 z1 | x2 y2], m2 = [z2 x3 | y3 z3]
    static void Transpose(double* p, out double4 x, out double4 y, out double4 z)
    {
        if (IsAvxSupported)
        {
            v256 m0 = *(v256*)p;
            v256 m1 = *(v256*)(p + 4);
            v256 m2 = *(v256*)(p + 8);

            v256 a = mm256_permute2f128_pd(m0, m1, 0x30); // [x0 y0 | x2 y2]
            v256 b = mm256_permute2f128_pd(m0, m2, 0x21); // [z0 x1 | z2 x3]
            v256 c = mm256_permute2f128_pd(m1, m2, 0x30); // [y1 z1 | y3 z3]

            x = ToDouble4(mm256_blend_pd(a, b, 0b1010));
            y = ToDouble4(mm256_shuffle_pd(a, c, 0b0101));
            z = ToDouble4(mm256_blend_pd(b, c, 0b1010));
            return;
        }

        double4x3 t = math.transpose(*(double3x4*)p);
        x = t.c0;
        y = t.c1;
        z = t.c2;
    }

    static double4 ToDouble4(v256 v) => new(v.Double0, v.Double1, v.Double2, v.Double3);

    public static void GetValues<T>(
        in T module,
        double* x,
        double* y,
        double* z,
        double* output,
        int count
    )
        where T : struct, IVectorModule
    {
        // Avoids a defensive copy of the module on every call.
        ref T m = ref Unsafe.AsRef(in module);
        for (int i = 0; i < count; i += 4)
        {
            var value = m.GetValue(Load(x, i, count), Load(y, i, count), Load(z, i, count));
            Store(output, i, count, value);
        }
    }

    public static void GetValues<T>(in T module, Vector3d* coords, double* output, int count)
        where T : struct, IVectorModule
    {
        ref T m = ref Unsafe.AsRef(in module);
        for (int i = 0; i < count; i += 4)
        {
            Load(coords, i, count, out var x, out var y, out var z);
            Store(output, i, count, m.GetValue(x, y, z));
        }
    }
}

public static unsafe class BatchModuleExtensions
{
    /// <summary>
    /// Evaluates <paramref name="module"/> at each of <paramref name="coords"/>.
    /// </summary>
    public static void GetValues<T>(
        ref this T module,
        MemorySpan<Vector3d> coords,
        MemorySpan<double> output
    )
        where T : struct, IBatchModule
    {
        if (coords.Length != output.Length)
            BurstException.ThrowArgumentOutOfRange();

        module.GetValues(coords.GetDataPtr(), output.GetDataPtr(), coords.Length);
    }

    /// <summary>
    /// Evaluates <paramref name="module"/> at each of <paramref name="coords"/>
    /// and returns the results in a new array.
    /// </summary>
    public static NativeArray<double> GetValues<T>(
        ref this T module,
        MemorySpan<Vector3d> coords,
        Allocator allocator
    )
        where T : struct, IBatchModule
    {
        var output = new NativeArray<double>(
            coords.Length,
            allocator,
            NativeArrayOptions.UninitializedMemory
        );
        module.GetValues(coords, new MemorySpan<double>(output));
        return output;
    }
}
