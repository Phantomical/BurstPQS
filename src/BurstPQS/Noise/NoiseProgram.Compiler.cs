using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BurstPQS.Util;
using LibNoise;
using LibNoise.Modifiers;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BurstPQS.Noise;

public unsafe partial struct NoiseProgram
{
    /// <summary>
    /// Compile <paramref name="module"/> into a <see cref="NoiseProgram"/>.
    /// Modules without a native implementation are evaluated by calling back
    /// into managed code.
    /// </summary>
    /// <remarks>
    /// The program copies all the parameters it needs out of the module, so
    /// later changes to the module are not reflected in the program. Modules
    /// using the managed fallback are the exception, since those are evaluated
    /// through the module itself.
    /// </remarks>
    public static NoiseProgram Compile(IModule module, Allocator allocator = Allocator.Persistent)
    {
        if (module is null)
            throw new ArgumentNullException(nameof(module));

        var compiler = new Compiler();
        try
        {
            compiler.Emit(module);
            return compiler.Finish(allocator);
        }
        catch
        {
            compiler.DisposeHandles();
            throw;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="module"/> and all its
    /// sources can be compiled without needing the managed fallback.
    /// </summary>
    public static bool IsSupported(IModule module)
    {
        if (module is null)
            return false;
        if (!NativeTypes.Contains(module.GetType()))
            return false;

        foreach (var source in GetSources(module))
        {
            if (!IsSupported(source))
                return false;
        }

        return true;
    }

    static readonly HashSet<Type> NativeTypes =
    [
        typeof(Constant),
        typeof(Perlin),
        typeof(Billow),
        typeof(RidgedMultifractal),
        typeof(FastNoise),
        typeof(FastBillow),
        typeof(FastRidgedMultifractal),
        typeof(Voronoi),
        typeof(Checkerboard),
        typeof(Cylinders),
        typeof(Spheres),
        typeof(Turbulence),
        typeof(FastTurbulence),
        typeof(AbsoluteOutput),
        typeof(BiasOutput),
        typeof(ScaleOutput),
        typeof(ScaleBiasOutput),
        typeof(ClampOutput),
        typeof(ExponentialOutput),
        typeof(InvertOutput),
        typeof(Terrace),
        typeof(Add),
        typeof(Multiply),
        typeof(LargerOutput),
        typeof(SmallerOutput),
        typeof(Power),
        typeof(Blend),
        typeof(Select),
        typeof(TranslateInput),
        typeof(ScaleInput),
        typeof(RotateInput),
        typeof(InvertInput),
        typeof(DisplaceInput),
    ];

    static IEnumerable<IModule> GetSources(IModule module)
    {
        return module switch
        {
            Turbulence m => [m.SourceModule],
            FastTurbulence m => [m.SourceModule],
            AbsoluteOutput m => [m.SourceModule],
            BiasOutput m => [m.SourceModule],
            ScaleOutput m => [m.SourceModule],
            ScaleBiasOutput m => [m.SourceModule],
            ClampOutput m => [m.SourceModule],
            ExponentialOutput m => [m.SourceModule],
            InvertOutput m => [m.SourceModule],
            Terrace m => [m.SourceModule],
            Add m => [m.SourceModule1, m.SourceModule2],
            Multiply m => [m.SourceModule1, m.SourceModule2],
            LargerOutput m => [m.SourceModule1, m.SourceModule2],
            SmallerOutput m => [m.SourceModule1, m.SourceModule2],
            Power m => [m.BaseModule, m.PowerModule],
            Blend m => [m.SourceModule1, m.SourceModule2, m.WeightModule],
            Select m => [m.SourceModule1, m.SourceModule2, m.ControlModule],
            TranslateInput m => [m.SourceModule],
            ScaleInput m => [m.SourceModule],
            RotateInput m => [m.SourceModule],
            InvertInput m => [m.SourceModule],
            DisplaceInput m =>
            [
                m.XDisplaceModule,
                m.YDisplaceModule,
                m.ZDisplaceModule,
                m.SourceModule,
            ],
            _ => [],
        };
    }

    sealed class Compiler
    {
        readonly List<NoiseInstruction> code = [];
        readonly List<int> handles = [];
        readonly HashSet<IModule> active = [];
        byte[] data = new byte[256];
        int dataLength = 0;

        int valueDepth = 0;
        int maxValueDepth = 0;
        int frameDepth = 0;
        int maxFrameDepth = 0;

        public void Emit(IModule module)
        {
            if (module is null)
                throw new ArgumentNullException(nameof(module), "noise module has a null source");

            if (!NativeTypes.Contains(module.GetType()))
            {
                EmitManaged(module);
                return;
            }

            if (!active.Add(module))
                throw new ArgumentException("noise module graph contains a cycle", nameof(module));

            EmitNative(module);
            active.Remove(module);
        }

        void EmitNative(IModule module)
        {
            if (TryEmitGenerator(module))
                return;

            switch (module)
            {
                // Turbulence displaces the input by three distortion noises,
                // each sampled at a fixed offset from the original coordinates.
                case Turbulence m:
                    Distortion(NoiseOp.Perlin, Alloc(new BurstPerlin(m.XDistort)), TurbulenceX);
                    Distortion(NoiseOp.Perlin, Alloc(new BurstPerlin(m.YDistort)), TurbulenceY);
                    Distortion(NoiseOp.Perlin, Alloc(new BurstPerlin(m.ZDistort)), TurbulenceZ);
                    Displaced(m.Power, m.SourceModule);
                    break;
                case FastTurbulence m:
                    Distortion(NoiseOp.FastNoise, AllocFastNoise(m.XDistort), TurbulenceX);
                    Distortion(NoiseOp.FastNoise, AllocFastNoise(m.YDistort), TurbulenceY);
                    Distortion(NoiseOp.FastNoise, AllocFastNoise(m.ZDistort), TurbulenceZ);
                    Displaced(m.Power, m.SourceModule);
                    break;

                case AbsoluteOutput m:
                    Unary(NoiseOp.Abs, 0, m.SourceModule);
                    break;
                case BiasOutput m:
                    Unary(NoiseOp.Bias, Alloc(m.Bias), m.SourceModule);
                    break;
                case ScaleOutput m:
                    Unary(NoiseOp.Scale, Alloc(m.Scale), m.SourceModule);
                    break;
                case ScaleBiasOutput m:
                    Unary(NoiseOp.ScaleBias, Alloc(new double2(m.Scale, m.Bias)), m.SourceModule);
                    break;
                case ClampOutput m:
                    Unary(
                        NoiseOp.Clamp,
                        Alloc(new double2(m.LowerBound, m.UpperBound)),
                        m.SourceModule
                    );
                    break;
                case ExponentialOutput m:
                    Unary(NoiseOp.Exponential, Alloc(m.Exponent), m.SourceModule);
                    break;
                case InvertOutput m:
                    Unary(NoiseOp.Negate, 0, m.SourceModule);
                    break;
                case Terrace m:
                    Unary(NoiseOp.Terrace, AllocTerrace(m), m.SourceModule);
                    break;

                case Add m:
                    Binary(NoiseOp.Add, m.SourceModule1, m.SourceModule2);
                    break;
                case Multiply m:
                    Binary(NoiseOp.Multiply, m.SourceModule1, m.SourceModule2);
                    break;
                case LargerOutput m:
                    Binary(NoiseOp.Max, m.SourceModule1, m.SourceModule2);
                    break;
                case SmallerOutput m:
                    Binary(NoiseOp.Min, m.SourceModule1, m.SourceModule2);
                    break;
                case Power m:
                    Binary(NoiseOp.Power, m.BaseModule, m.PowerModule);
                    break;

                case Blend m:
                    Emit(m.SourceModule1);
                    Emit(m.SourceModule2);
                    Emit(m.WeightModule);
                    Op(NoiseOp.Blend, 0, values: -2);
                    break;
                case Select m:
                    Emit(m.SourceModule1);
                    Emit(m.SourceModule2);
                    Emit(m.ControlModule);
                    Op(
                        NoiseOp.Select,
                        Alloc(
                            new SelectData
                            {
                                Lower = m.LowerBound,
                                Upper = m.UpperBound,
                                EdgeFalloff = m.EdgeFalloff,
                            }
                        ),
                        values: -2
                    );
                    break;

                case TranslateInput m:
                    Transformed(
                        NoiseOp.PushTranslate,
                        Alloc(new double3(m.X, m.Y, m.Z)),
                        m.SourceModule
                    );
                    break;
                case ScaleInput m:
                    Transformed(
                        NoiseOp.PushScale,
                        Alloc(new double3(m.X, m.Y, m.Z)),
                        m.SourceModule
                    );
                    break;
                case RotateInput m:
                    Transformed(
                        NoiseOp.PushRotate,
                        Alloc(
                            new RotateData
                            {
                                x1 = m.m_x1Matrix,
                                y1 = m.m_y1Matrix,
                                z1 = m.m_z1Matrix,
                                x2 = m.m_x2Matrix,
                                y2 = m.m_y2Matrix,
                                z2 = m.m_z2Matrix,
                                x3 = m.m_x3Matrix,
                                y3 = m.m_y3Matrix,
                                z3 = m.m_z3Matrix,
                            }
                        ),
                        m.SourceModule
                    );
                    break;
                case InvertInput m:
                    Transformed(NoiseOp.PushNegate, 0, m.SourceModule);
                    break;

                // Each displacement sees the coordinates as already displaced
                // by the previous ones.
                case DisplaceInput m:
                    Op(NoiseOp.PushCopy, 0, frames: 1);
                    Emit(m.XDisplaceModule);
                    Op(NoiseOp.DisplaceX, 0, values: -1);
                    Emit(m.YDisplaceModule);
                    Op(NoiseOp.DisplaceY, 0, values: -1);
                    Emit(m.ZDisplaceModule);
                    Op(NoiseOp.DisplaceZ, 0, values: -1);
                    Emit(m.SourceModule);
                    Op(NoiseOp.PopFrame, 0, frames: -1);
                    break;

                default:
                    throw new NotImplementedException(
                        $"NoiseProgram is missing an implementation for {module.GetType().Name}"
                    );
            }
        }

        // Kept out of EmitNative so that the large noise structs constructed
        // here don't take up stack space in each level of the recursion.
        [MethodImpl(MethodImplOptions.NoInlining)]
        bool TryEmitGenerator(IModule module)
        {
            switch (module)
            {
                case Constant m:
                    Generator(NoiseOp.Constant, Alloc(m.Value));
                    break;
                case Perlin m:
                    Generator(NoiseOp.Perlin, Alloc(new BurstPerlin(m)));
                    break;
                case Billow m:
                    Generator(NoiseOp.Billow, Alloc(new BurstBillow(m)));
                    break;
                case RidgedMultifractal m:
                    Generator(NoiseOp.RidgedMultifractal, Alloc(new BurstRidgedMultifractal(m)));
                    break;
                case FastNoise m:
                    Generator(NoiseOp.FastNoise, Alloc(new BurstFastNoise(m)));
                    break;
                case FastBillow m:
                    Generator(NoiseOp.FastBillow, Alloc(new BurstFastBillow(m)));
                    break;
                case FastRidgedMultifractal m:
                    Generator(
                        NoiseOp.FastRidgedMultifractal,
                        Alloc(new BurstFastRidgedMultifractal(m))
                    );
                    break;
                case Voronoi m:
                    Generator(NoiseOp.Voronoi, Alloc(new BurstVoronoi(m)));
                    break;
                case LibNoise.Checkerboard:
                    Generator(NoiseOp.Checkerboard, Alloc(new BurstCheckerboard()));
                    break;
                case Cylinders m:
                    Generator(NoiseOp.Cylinders, Alloc(new BurstCylinders(m)));
                    break;
                case Spheres m:
                    Generator(NoiseOp.Spheres, Alloc(new BurstSpheres(m)));
                    break;

                default:
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        int AllocFastNoise(FastNoise noise) => Alloc(new BurstFastNoise(noise));

        static readonly double3 TurbulenceX = new(
            0.189422607421875,
            0.99371337890625,
            0.4781646728515625
        );
        static readonly double3 TurbulenceY = new(
            0.4046478271484375,
            0.276611328125,
            0.9230499267578125
        );
        static readonly double3 TurbulenceZ = new(
            0.82122802734375,
            0.1710968017578125,
            0.6842803955078125
        );

        void Distortion(NoiseOp op, int noise, double3 offset)
        {
            Op(NoiseOp.PushTranslate, Alloc(offset), frames: 1);
            Generator(op, noise);
            Op(NoiseOp.PopFrame, 0, frames: -1);
        }

        void Displaced(double power, IModule source)
        {
            Op(NoiseOp.PushDisplace, Alloc(power), values: -3, frames: 1);
            Emit(source);
            Op(NoiseOp.PopFrame, 0, frames: -1);
        }

        void EmitManaged(IModule module)
        {
            ManagedNoise.EnsureInitialized();

            int offset = Alloc(new ObjectHandle<IModule>(module));
            handles.Add(offset);
            Generator(NoiseOp.Managed, offset);
        }

        void Generator(NoiseOp op, int offset) => Op(op, offset, values: 1);

        void Unary(NoiseOp op, int offset, IModule source)
        {
            Emit(source);
            Op(op, offset);
        }

        void Binary(NoiseOp op, IModule a, IModule b)
        {
            Emit(a);
            Emit(b);
            Op(op, 0, values: -1);
        }

        void Transformed(NoiseOp op, int offset, IModule source)
        {
            Op(op, offset, frames: 1);
            Emit(source);
            Op(NoiseOp.PopFrame, 0, frames: -1);
        }

        void Op(NoiseOp op, int offset, int values = 0, int frames = 0)
        {
            code.Add(new(op, offset));

            valueDepth += values;
            frameDepth += frames;
            maxValueDepth = System.Math.Max(maxValueDepth, valueDepth);
            maxFrameDepth = System.Math.Max(maxFrameDepth, frameDepth);
        }

        int AllocTerrace(Terrace m)
        {
            var points = m.ControlPoints;
            if (points.Count < 2)
                throw new ArgumentException("Terrace module must have at least 2 control points");

            int offset = Alloc(new TerraceData { Count = points.Count, Invert = m.InvertTerraces });
            foreach (var point in points)
                Append(point);
            return offset;
        }

        int Alloc<T>(T value)
            where T : unmanaged
        {
            int offset = Align(dataLength, 16);
            Reserve(offset + sizeof(T));
            fixed (byte* p = data)
                *(T*)(p + offset) = value;
            dataLength = offset + sizeof(T);
            return offset;
        }

        // Writes value directly after the previous allocation.
        void Append<T>(T value)
            where T : unmanaged
        {
            int offset = Align(dataLength, UnsafeUtility.AlignOf<T>());
            Reserve(offset + sizeof(T));
            fixed (byte* p = data)
                *(T*)(p + offset) = value;
            dataLength = offset + sizeof(T);
        }

        void Reserve(int length)
        {
            if (length <= data.Length)
                return;

            var next = new byte[System.Math.Max(length, data.Length * 2)];
            Buffer.BlockCopy(data, 0, next, 0, dataLength);
            data = next;
        }

        static int Align(int value, int align) => (value + align - 1) & ~(align - 1);

        public NoiseProgram Finish(Allocator allocator)
        {
            if (valueDepth != 1 || frameDepth != 0)
                throw new InvalidOperationException(
                    $"NoiseProgram compiled to an unbalanced stack (values: {valueDepth}, frames: {frameDepth})"
                );

            int codeBytes = Align(code.Count * sizeof(NoiseInstruction), 16);
            var block = (byte*)UnsafeUtility.Malloc(codeBytes + dataLength, 16, allocator);

            var program = new NoiseProgram
            {
                code = (NoiseInstruction*)block,
                data = block + codeBytes,
                codeLength = code.Count,
                valueDepth = maxValueDepth,
                frameDepth = maxFrameDepth,
                usesManagedFallback = handles.Count != 0,
                allocator = allocator,
            };

            for (int i = 0; i < code.Count; ++i)
                program.code[i] = code[i];
            fixed (byte* p = data)
                UnsafeUtility.MemCpy(program.data, p, dataLength);

            return program;
        }

        public void DisposeHandles()
        {
            fixed (byte* p = data)
            {
                foreach (var offset in handles)
                    ((ObjectHandle<IModule>*)(p + offset))->Dispose();
            }
            handles.Clear();
        }
    }
}
