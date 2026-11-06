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

// Implements IBatchModule for IVectorModules, 4 points at a time. Any
// leftover points are padded with zeros and evaluated as one more group.
internal static unsafe class BatchUtil
{
    // Loads p[i..i + 4). There must be at least 4 values left.
    public static double4 Load4(double* p, int i) => *(double4*)(p + i);

    // Stores v to p[i..i + 4). There must be room for all 4 values.
    public static void Store4(double* p, int i, double4 v) => *(double4*)(p + i) = v;

    // Loads n < 4 values, padding the rest with zeros.
    public static double4 LoadPartial(double* p, int n)
    {
        double4 v = 0.0;
        for (int j = 0; j < n; ++j)
            v[j] = p[j];
        return v;
    }

    public static void StorePartial(double* p, int n, double4 v)
    {
        for (int j = 0; j < n; ++j)
            p[j] = v[j];
    }

    public static void LoadPartial(Vector3d* p, int n, out double4 x, out double4 y, out double4 z)
    {
        x = 0.0;
        y = 0.0;
        z = 0.0;
        for (int j = 0; j < n; ++j)
        {
            x[j] = p[j].x;
            y[j] = p[j].y;
            z[j] = p[j].z;
        }
    }

    // Loads 4 Vector3ds and transposes them into one vector per axis.
    //
    // As 3 vectors the points are
    //   m0 = [x0 y0 | z0 x1], m1 = [y1 z1 | x2 y2], m2 = [z2 x3 | y3 z3]
    public static void Transpose(Vector3d* coords, out double4 x, out double4 y, out double4 z)
    {
        double* p = (double*)coords;
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

        // 4 consecutive Vector3ds have the layout of a double3x4. LLVM turns
        // this into scalar loads, which is why the AVX path is done by hand.
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

        int i = 0;
        for (; i + 4 <= count; i += 4)
        {
            Store4(output, i, m.GetValue(Load4(x, i), Load4(y, i), Load4(z, i)));
        }

        int rem = count - i;
        if (rem > 0)
        {
            var value = m.GetValue(
                LoadPartial(x + i, rem),
                LoadPartial(y + i, rem),
                LoadPartial(z + i, rem)
            );
            StorePartial(output + i, rem, value);
        }
    }

    public static void GetValues<T>(in T module, Vector3d* coords, double* output, int count)
        where T : struct, IVectorModule
    {
        ref T m = ref Unsafe.AsRef(in module);

        int i = 0;
        for (; i + 4 <= count; i += 4)
        {
            Transpose(coords + i, out var x, out var y, out var z);
            Store4(output, i, m.GetValue(x, y, z));
        }

        int rem = count - i;
        if (rem > 0)
        {
            LoadPartial(coords + i, rem, out var x, out var y, out var z);
            StorePartial(output + i, rem, m.GetValue(x, y, z));
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
