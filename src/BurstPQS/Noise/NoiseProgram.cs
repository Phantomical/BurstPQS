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
using static BurstPQS.Noise.BatchUtil;
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
                int i = 0;
                for (; i + 4 <= n; i += 4)
                {
                    BatchUtil.Transpose(input + i, out var vx, out var vy, out var vz);
                    Store4(owned[0].x, i, vx);
                    Store4(owned[0].y, i, vy);
                    Store4(owned[0].z, i, vz);
                }
                for (; i < n; ++i)
                {
                    owned[0].x[i] = input[i].x;
                    owned[0].y[i] = input[i].y;
                    owned[0].z[i] = input[i].z;
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
                    Ops.Constant(values[vsp++], *(double*)d, n);
                    break;

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
                    Ops.Abs(values[vsp - 1], n);
                    break;
                case NoiseOp.Bias:
                    Ops.Bias(values[vsp - 1], *(double*)d, n);
                    break;
                case NoiseOp.Scale:
                    Ops.Scale(values[vsp - 1], *(double*)d, n);
                    break;
                case NoiseOp.ScaleBias:
                    Ops.ScaleBias(values[vsp - 1], *(double2*)d, n);
                    break;
                case NoiseOp.Clamp:
                    Ops.Clamp(values[vsp - 1], *(double2*)d, n);
                    break;
                case NoiseOp.Exponential:
                    Ops.Exponential(values[vsp - 1], *(double*)d, n);
                    break;
                case NoiseOp.Negate:
                    Ops.Negate(values[vsp - 1], n);
                    break;
                case NoiseOp.Terrace:
                    Ops.Terrace(values[vsp - 1], (TerraceData*)d, n);
                    break;
                #endregion

                #region Binary
                case NoiseOp.Add:
                    vsp--;
                    Ops.Add(values[vsp - 1], values[vsp], n);
                    break;
                case NoiseOp.Multiply:
                    vsp--;
                    Ops.Multiply(values[vsp - 1], values[vsp], n);
                    break;
                case NoiseOp.Max:
                    vsp--;
                    Ops.Max(values[vsp - 1], values[vsp], n);
                    break;
                case NoiseOp.Min:
                    vsp--;
                    Ops.Min(values[vsp - 1], values[vsp], n);
                    break;
                case NoiseOp.Power:
                    vsp--;
                    Ops.Power(values[vsp - 1], values[vsp], n);
                    break;
                #endregion

                #region Ternary
                case NoiseOp.Blend:
                    vsp -= 2;
                    Ops.Blend(values[vsp - 1], values[vsp], values[vsp + 1], n);
                    break;
                case NoiseOp.Select:
                    vsp -= 2;
                    Ops.Select(values[vsp - 1], values[vsp], values[vsp + 1], (SelectData*)d, n);
                    break;
                #endregion

                #region Frames
                case NoiseOp.PushTranslate:
                    Ops.Translate(f, owned[++fsp], *(double3*)d, n);
                    frames[fsp] = owned[fsp];
                    break;
                case NoiseOp.PushScale:
                    Ops.ScaleInput(f, owned[++fsp], *(double3*)d, n);
                    frames[fsp] = owned[fsp];
                    break;
                case NoiseOp.PushRotate:
                    Ops.Rotate(f, owned[++fsp], (RotateData*)d, n);
                    frames[fsp] = owned[fsp];
                    break;
                case NoiseOp.PushNegate:
                    Ops.NegateInput(f, owned[++fsp], n);
                    frames[fsp] = owned[fsp];
                    break;
                case NoiseOp.PushCopy:
                    frames[++fsp] = f;
                    break;

                // These write to the frame's own storage since the frame may
                // still alias its parent after a PushCopy.
                case NoiseOp.DisplaceX:
                    Ops.Displace(owned[fsp].x, f.x, values[--vsp], n);
                    frames[fsp].x = owned[fsp].x;
                    break;
                case NoiseOp.DisplaceY:
                    Ops.Displace(owned[fsp].y, f.y, values[--vsp], n);
                    frames[fsp].y = owned[fsp].y;
                    break;
                case NoiseOp.DisplaceZ:
                    Ops.Displace(owned[fsp].z, f.z, values[--vsp], n);
                    frames[fsp].z = owned[fsp].z;
                    break;
                case NoiseOp.PushDisplace:
                    vsp -= 3;
                    Ops.Displace(
                        f,
                        owned[++fsp],
                        values[vsp],
                        values[vsp + 1],
                        values[vsp + 2],
                        *(double*)d,
                        n
                    );
                    frames[fsp] = owned[fsp];
                    break;
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

    // Kept out of Run so that each generator's kernel is inlined into its own
    // loop, which LLVM won't do once Run has grown this large.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Generate<T>(T* noise, in Frame f, double* o, int n)
        where T : unmanaged, IBatchModule => noise->GetValues(f.x, f.y, f.z, o, n);

    internal struct TerraceData
    {
        public int Count;
        public bool Invert;
    }

    internal struct SelectData
    {
        public double Lower;
        public double Upper;
        public double EdgeFalloff;
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
