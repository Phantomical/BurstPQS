using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackgroundResourceProcessing.Shim;
using BurstPQS.Collections;
using BurstPQS.Util;
using LibNoise;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static BurstPQS.Noise.NoiseUtil;

namespace BurstPQS.Noise;

internal enum NoiseOp : int
{
    // Generators: read the top frame, push a value.
    Constant,
    Perlin,
    Billow,
    RidgedMultifractal,
    FastNoise,
    FastBillow,
    FastRidgedMultifractal,
    Voronoi,
    Checkerboard,
    Cylinders,
    Spheres,
    Managed,

    // Unary: transform the top value in place.
    Abs,
    Bias,
    Scale,
    ScaleBias,
    Clamp,
    Exponential,
    Negate,
    Terrace,

    // Binary: pop b, combine into a.
    Add,
    Multiply,
    Max,
    Min,
    Power,

    // Ternary: pop b and the control/weight, combine into a.
    Blend,
    Select,

    // Frame ops.
    PushTranslate,
    PushScale,
    PushRotate,
    PushNegate,
    PushCopy,
    DisplaceX,
    DisplaceY,
    DisplaceZ,
    PushDisplace,
    PopFrame,
}

internal struct NoiseInstruction(NoiseOp op, int data)
{
    public NoiseOp Op = op;
    public int Data = data;
}

/// <summary>
/// A compiled libnoise <see cref="IModule"/> tree that can be evaluated from
/// Burst over whole arrays of coordinates at once.
/// </summary>
///
/// <remarks>
/// <para>
/// The module tree is flattened into a postfix instruction stream for a stack
/// machine. Each stack slot holds a block of values rather than a single one,
/// so every instruction runs as a tight loop over the block and the dispatch
/// cost is amortized across it. Modules that change their input coordinates
/// (e.g. <see cref="LibNoise.Modifiers.TranslateInput"/>) push a new frame
/// of coordinates onto a separate frame stack for their sources to read.
/// </para>
/// <para>
/// All per-instruction parameters live in a single allocation along with the
/// instruction array, and instructions refer to them by offset.
/// </para>
/// <para>
/// Every module that Kopernicus can load is implemented natively. Any other
/// module is evaluated by calling back into its managed implementation, which
/// is a lot slower. Check <see cref="UsesManagedFallback"/> to see if that
/// happened.
/// </para>
/// <para>
/// This must be disposed when no longer needed.
/// </para>
/// </remarks>
public unsafe partial struct NoiseProgram : IBatchModule, IDisposable
{
    const int ChunkSize = 128;

    [NativeDisableUnsafePtrRestriction]
    NoiseInstruction* code;

    [NativeDisableUnsafePtrRestriction]
    byte* data;

    int codeLength;
    int valueDepth;
    int frameDepth;
    bool usesManagedFallback;
    Allocator allocator;

    public readonly bool IsCreated => code is not null;

    /// <summary>
    /// Whether part of this program calls back into a managed
    /// <see cref="IModule"/> implementation.
    /// </summary>
    public readonly bool UsesManagedFallback => usesManagedFallback;

    #region Evaluation
    struct Frame
    {
        public double* x;
        public double* y;
        public double* z;
    }

    public readonly double Evaluate(double x, double y, double z)
    {
        double result;
        Evaluate(&x, &y, &z, &result, 1);
        return result;
    }

    public readonly double Evaluate(double3 coord) => Evaluate(coord.x, coord.y, coord.z);

    public readonly double Evaluate(Vector3d coord) => Evaluate(coord.x, coord.y, coord.z);

    public readonly void Evaluate(MemorySpan<Vector3d> coords, MemorySpan<double> output)
    {
        if (coords.Length != output.Length)
            BurstException.ThrowArgumentOutOfRange();

        Execute(
            (double3*)coords.GetDataPtr(),
            null,
            null,
            null,
            output.GetDataPtr(),
            output.Length
        );
    }

    /// <summary>
    /// Evaluates the noise at each of <paramref name="coords"/> and returns
    /// the results in a new array.
    /// </summary>
    public readonly NativeArray<double> Evaluate(MemorySpan<Vector3d> coords, Allocator allocator)
    {
        var output = new NativeArray<double>(
            coords.Length,
            allocator,
            NativeArrayOptions.UninitializedMemory
        );
        Evaluate(coords, new MemorySpan<double>(output));
        return output;
    }

    public readonly void Evaluate(MemorySpan<double3> coords, MemorySpan<double> output)
    {
        if (coords.Length != output.Length)
            BurstException.ThrowArgumentOutOfRange();

        Execute(coords.GetDataPtr(), null, null, null, output.GetDataPtr(), output.Length);
    }

    public readonly void Evaluate(
        MemorySpan<double> x,
        MemorySpan<double> y,
        MemorySpan<double> z,
        MemorySpan<double> output
    )
    {
        int count = output.Length;
        if (x.Length != count || y.Length != count || z.Length != count)
            BurstException.ThrowArgumentOutOfRange();

        Execute(null, x.GetDataPtr(), y.GetDataPtr(), z.GetDataPtr(), output.GetDataPtr(), count);
    }

    public readonly void Evaluate(double* x, double* y, double* z, double* output, int count)
    {
        if (count < 0)
            BurstException.ThrowArgumentOutOfRange();

        Execute(null, x, y, z, output, count);
    }

    public readonly void GetValues(double* x, double* y, double* z, double* output, int count) =>
        Evaluate(x, y, z, output, count);

    public readonly void GetValues(Vector3d* coords, double* output, int count)
    {
        if (count < 0)
            BurstException.ThrowArgumentOutOfRange();

        Execute((double3*)coords, null, null, null, output, count);
    }

    // Either aos is non-null or x, y, and z are.
    readonly void Execute(double3* aos, double* x, double* y, double* z, double* output, int count)
    {
        if (count == 0)
            return;
        if (code is null)
            throw new InvalidOperationException("NoiseProgram has not been created");

        int chunk = math.min(count, ChunkSize);
        int frameSlots = frameDepth + 1;

        // Value slot 0 is always the output itself, so only the rest need storage.
        // Frame slot 0 is only used when converting from AoS input.
        int bufferCount = (valueDepth - 1) + 3 * frameSlots;
        int size =
            bufferCount * chunk * sizeof(double)
            + valueDepth * sizeof(double*)
            + 2 * frameSlots * sizeof(Frame);

        using var scratch = new OwnedBuffer(size, Allocator.Temp);

        var values = (double**)scratch.Data;
        var frames = (Frame*)(values + valueDepth);
        var owned = frames + frameSlots;
        var buffers = (double*)(owned + frameSlots);

        for (int i = 1; i < valueDepth; ++i)
        {
            values[i] = buffers;
            buffers += chunk;
        }
        for (int i = 0; i < frameSlots; ++i)
        {
            owned[i].x = buffers;
            owned[i].y = buffers + chunk;
            owned[i].z = buffers + 2 * chunk;
            buffers += 3 * chunk;
        }

        for (int start = 0; start < count; start += chunk)
        {
            int n = math.min(chunk, count - start);

            if (aos is not null)
            {
                var input = (Vector3d*)(aos + start);
                for (int i = 0; i < n; i += 4)
                {
                    BatchUtil.Load(input, i, n, out var vx, out var vy, out var vz);
                    BatchUtil.Store(owned[0].x, i, n, vx);
                    BatchUtil.Store(owned[0].y, i, n, vy);
                    BatchUtil.Store(owned[0].z, i, n, vz);
                }
                frames[0] = owned[0];
            }
            else
            {
                frames[0].x = x + start;
                frames[0].y = y + start;
                frames[0].z = z + start;
            }

            values[0] = output + start;
            Run(values, frames, owned, n);
        }
    }

    readonly void Run(double** values, Frame* frames, Frame* owned, int n)
    {
        int vsp = 0;
        int fsp = 0;

        for (int pc = 0; pc < codeLength; ++pc)
        {
            var inst = code[pc];
            var d = data + inst.Data;
            var f = frames[fsp];

            switch (inst.Op)
            {
                #region Generators
                case NoiseOp.Constant:
                {
                    var o = values[vsp++];
                    double v = *(double*)d;
                    for (int i = 0; i < n; ++i)
                        o[i] = v;
                    break;
                }

                case NoiseOp.Perlin:
                    Generate((BurstPerlin*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.Billow:
                    Generate((BurstBillow*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.RidgedMultifractal:
                    Generate((BurstRidgedMultifractal*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.FastNoise:
                    Generate((BurstFastNoise*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.FastBillow:
                    Generate((BurstFastBillow*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.FastRidgedMultifractal:
                    Generate((BurstFastRidgedMultifractal*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.Voronoi:
                    Generate((BurstVoronoi*)d, f, values[vsp++], n);
                    break;

                case NoiseOp.Checkerboard:
                    Generate((BurstCheckerboard*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.Cylinders:
                    Generate((BurstCylinders*)d, f, values[vsp++], n);
                    break;
                case NoiseOp.Spheres:
                    Generate((BurstSpheres*)d, f, values[vsp++], n);
                    break;

                case NoiseOp.Managed:
                    ManagedNoise.Evaluate(d, f.x, f.y, f.z, values[vsp++], n);
                    break;
                #endregion

                #region Unary
                case NoiseOp.Abs:
                {
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] = math.abs(a[i]);
                    break;
                }

                case NoiseOp.Bias:
                {
                    var a = values[vsp - 1];
                    double bias = *(double*)d;
                    for (int i = 0; i < n; ++i)
                        a[i] += bias;
                    break;
                }

                case NoiseOp.Scale:
                {
                    var a = values[vsp - 1];
                    double scale = *(double*)d;
                    for (int i = 0; i < n; ++i)
                        a[i] *= scale;
                    break;
                }

                case NoiseOp.ScaleBias:
                {
                    var a = values[vsp - 1];
                    var p = (double2*)d;
                    double scale = p->x;
                    double bias = p->y;
                    for (int i = 0; i < n; ++i)
                        a[i] = a[i] * scale + bias;
                    break;
                }

                case NoiseOp.Clamp:
                {
                    var a = values[vsp - 1];
                    var p = (double2*)d;
                    double lower = p->x;
                    double upper = p->y;
                    for (int i = 0; i < n; ++i)
                    {
                        double v = a[i];
                        if (v < lower)
                            v = lower;
                        else if (v > upper)
                            v = upper;
                        a[i] = v;
                    }
                    break;
                }

                case NoiseOp.Exponential:
                {
                    var a = values[vsp - 1];
                    double exponent = *(double*)d;
                    for (int i = 0; i < n; ++i)
                        a[i] = math.pow(math.abs((a[i] + 1.0) / 2.0), exponent) * 2.0 - 1.0;
                    break;
                }

                case NoiseOp.Negate:
                {
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] = 0.0 - a[i];
                    break;
                }

                case NoiseOp.Terrace:
                {
                    var a = values[vsp - 1];
                    var p = (TerraceData*)d;
                    for (int i = 0; i < n; ++i)
                        a[i] = Terrace(p, a[i]);
                    break;
                }
                #endregion

                #region Binary
                case NoiseOp.Add:
                {
                    var b = values[--vsp];
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] += b[i];
                    break;
                }

                case NoiseOp.Multiply:
                {
                    var b = values[--vsp];
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] *= b[i];
                    break;
                }

                case NoiseOp.Max:
                {
                    var b = values[--vsp];
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] = a[i] > b[i] ? a[i] : b[i];
                    break;
                }

                case NoiseOp.Min:
                {
                    var b = values[--vsp];
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] = a[i] < b[i] ? a[i] : b[i];
                    break;
                }

                case NoiseOp.Power:
                {
                    var b = values[--vsp];
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] = math.pow(a[i], b[i]);
                    break;
                }
                #endregion

                #region Ternary
                case NoiseOp.Blend:
                {
                    var w = values[--vsp];
                    var b = values[--vsp];
                    var a = values[vsp - 1];
                    for (int i = 0; i < n; ++i)
                        a[i] = LinearInterpolate(a[i], b[i], (w[i] + 1.0) / 2.0);
                    break;
                }

                case NoiseOp.Select:
                {
                    var c = values[--vsp];
                    var b = values[--vsp];
                    var a = values[vsp - 1];
                    var p = (SelectData*)d;
                    for (int i = 0; i < n; ++i)
                        a[i] = Select(p, c[i], a[i], b[i]);
                    break;
                }
                #endregion

                #region Frames
                case NoiseOp.PushTranslate:
                {
                    var o = owned[++fsp];
                    var t = *(double3*)d;
                    for (int i = 0; i < n; ++i)
                    {
                        o.x[i] = f.x[i] + t.x;
                        o.y[i] = f.y[i] + t.y;
                        o.z[i] = f.z[i] + t.z;
                    }
                    frames[fsp] = o;
                    break;
                }

                case NoiseOp.PushScale:
                {
                    var o = owned[++fsp];
                    var s = *(double3*)d;
                    for (int i = 0; i < n; ++i)
                    {
                        o.x[i] = f.x[i] * s.x;
                        o.y[i] = f.y[i] * s.y;
                        o.z[i] = f.z[i] * s.z;
                    }
                    frames[fsp] = o;
                    break;
                }

                case NoiseOp.PushRotate:
                {
                    var o = owned[++fsp];
                    var m = (RotateData*)d;
                    for (int i = 0; i < n; ++i)
                    {
                        double x = f.x[i];
                        double y = f.y[i];
                        double z = f.z[i];
                        o.x[i] = m->x1 * x + m->y1 * y + m->z1 * z;
                        o.y[i] = m->x2 * x + m->y2 * y + m->z2 * z;
                        o.z[i] = m->x3 * x + m->y3 * y + m->z3 * z;
                    }
                    frames[fsp] = o;
                    break;
                }

                case NoiseOp.PushNegate:
                {
                    var o = owned[++fsp];
                    for (int i = 0; i < n; ++i)
                    {
                        o.x[i] = 0.0 - f.x[i];
                        o.y[i] = 0.0 - f.y[i];
                        o.z[i] = 0.0 - f.z[i];
                    }
                    frames[fsp] = o;
                    break;
                }

                case NoiseOp.PushCopy:
                    frames[++fsp] = f;
                    break;

                // These write to the frame's own storage since the frame may
                // still alias its parent after a PushCopy.
                case NoiseOp.DisplaceX:
                {
                    var v = values[--vsp];
                    var o = owned[fsp].x;
                    for (int i = 0; i < n; ++i)
                        o[i] = f.x[i] + v[i];
                    frames[fsp].x = o;
                    break;
                }

                case NoiseOp.DisplaceY:
                {
                    var v = values[--vsp];
                    var o = owned[fsp].y;
                    for (int i = 0; i < n; ++i)
                        o[i] = f.y[i] + v[i];
                    frames[fsp].y = o;
                    break;
                }

                case NoiseOp.DisplaceZ:
                {
                    var v = values[--vsp];
                    var o = owned[fsp].z;
                    for (int i = 0; i < n; ++i)
                        o[i] = f.z[i] + v[i];
                    frames[fsp].z = o;
                    break;
                }

                case NoiseOp.PushDisplace:
                {
                    var dz = values[--vsp];
                    var dy = values[--vsp];
                    var dx = values[--vsp];
                    var o = owned[++fsp];
                    double power = *(double*)d;
                    for (int i = 0; i < n; ++i)
                    {
                        o.x[i] = f.x[i] + dx[i] * power;
                        o.y[i] = f.y[i] + dy[i] * power;
                        o.z[i] = f.z[i] + dz[i] * power;
                    }
                    frames[fsp] = o;
                    break;
                }

                case NoiseOp.PopFrame:
                    fsp--;
                    break;
                #endregion

                default:
                    BurstException.ThrowArgumentOutOfRange();
                    break;
            }
        }
    }

    static void Generate<T>(T* noise, in Frame f, double* o, int n)
        where T : unmanaged, IBatchModule => noise->GetValues(f.x, f.y, f.z, o, n);

    internal struct TerraceData
    {
        public int Count;
        public bool Invert;
    }

    static double Terrace(TerraceData* p, double value)
    {
        int count = p->Count;
        // The control points directly follow the header.
        double* points = (double*)(p + 1);

        int i = 0;
        while (i < count && !(value < points[i]))
            i++;

        int lo = math.clamp(i - 1, 0, count - 1);
        int hi = math.clamp(i, 0, count - 1);
        if (lo == hi)
            return points[hi];

        double a = points[lo];
        double b = points[hi];
        double t = (value - a) / (b - a);
        if (p->Invert)
        {
            t = 1.0 - t;
            (a, b) = (b, a);
        }
        t *= t;
        return LinearInterpolate(a, b, t);
    }

    internal struct SelectData
    {
        public double Lower;
        public double Upper;
        public double EdgeFalloff;
    }

    static double Select(SelectData* p, double control, double a, double b)
    {
        double lower = p->Lower;
        double upper = p->Upper;
        double falloff = p->EdgeFalloff;

        if (falloff > 0.0)
        {
            if (control < lower - falloff)
                return a;
            if (control < lower + falloff)
            {
                double lo = lower - falloff;
                double hi = lower + falloff;
                return LinearInterpolate(a, b, SCurve3((control - lo) / (hi - lo)));
            }
            if (control < upper - falloff)
                return b;
            if (control < upper + falloff)
            {
                double lo = upper - falloff;
                double hi = upper + falloff;
                return LinearInterpolate(b, a, SCurve3((control - lo) / (hi - lo)));
            }
            return a;
        }

        if (!(control < lower) && control <= upper)
            return b;
        return a;
    }

    internal struct RotateData
    {
        public double x1,
            y1,
            z1;
        public double x2,
            y2,
            z2;
        public double x3,
            y3,
            z3;
    }
    #endregion

    #region Disposal
    public void Dispose()
    {
        if (code is null)
            return;

        try
        {
            if (usesManagedFallback)
            {
                for (int pc = 0; pc < codeLength; ++pc)
                {
                    if (code[pc].Op == NoiseOp.Managed)
                        ((ObjectHandle<IModule>*)(data + code[pc].Data))->Dispose();
                }
            }

            UnsafeUtility.Free(code, allocator);
        }
        finally
        {
            this = default;
        }
    }

    public JobHandle Dispose(JobHandle dependsOn)
    {
        var handle = new DisposeJob { program = this }.Schedule(dependsOn);
        this = default;
        return handle;
    }

    // Not burst-compiled since freeing the GC handles requires managed code.
    struct DisposeJob : IJob
    {
        public NoiseProgram program;

        public void Execute() => program.Dispose();
    }
    #endregion
}

/// <summary>
/// Calls back into a managed <see cref="IModule"/> from Burst.
/// </summary>
internal static unsafe class ManagedNoise
{
    internal delegate void EvaluateFn(
        void* handle,
        double* x,
        double* y,
        double* z,
        double* output,
        int count
    );

    struct VTable
    {
        public FunctionPointer<EvaluateFn> Evaluate;
    }

    static readonly SharedStaticShim<VTable> VTableStatic;

    // Keeps the delegate behind the function pointer alive.
    static EvaluateFn EvaluateDelegate;

    static ManagedNoise()
    {
        VTableStatic = SharedStaticShim<VTable>.GetOrCreate<VTable>();
        Init();
    }

    [BurstDiscard]
    static void Init()
    {
        EvaluateDelegate = EvaluateManaged;
        VTableStatic.Data.Evaluate = new(Marshal.GetFunctionPointerForDelegate(EvaluateDelegate));
    }

    internal static void EnsureInitialized() { }

    public static void Evaluate(void* handle, double* x, double* y, double* z, double* o, int n)
    {
        if (BurstUtil.IsBurstCompiled)
            VTableStatic.Data.Evaluate.Invoke(handle, x, y, z, o, n);
        else
            EvaluateDirect(handle, x, y, z, o, n);
    }

    [BurstDiscard]
    static void EvaluateDirect(void* handle, double* x, double* y, double* z, double* o, int n) =>
        EvaluateManaged(handle, x, y, z, o, n);

    [AOT.MonoPInvokeCallback(typeof(EvaluateFn))]
    static void EvaluateManaged(void* handle, double* x, double* y, double* z, double* o, int n)
    {
        try
        {
            var module = ((ObjectHandle<IModule>*)handle)->Target;
            for (int i = 0; i < n; ++i)
                o[i] = module.GetValue(x[i], y[i], z[i]);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            for (int i = 0; i < n; ++i)
                o[i] = 0.0;
        }
    }
}
