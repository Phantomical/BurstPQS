using System;
using System.Collections.Generic;
using BurstPQS.Collections;
using BurstPQS.Noise;
using KSP.Testing;
using LibNoise;
using LibNoise.Modifiers;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BurstPQS.Test.Noise;

/// <summary>
/// Validates <see cref="NoiseProgram"/> against the LibNoise modules it was
/// compiled from, both when run from managed code and from a Burst job.
/// </summary>
public class NoiseProgramTests : BurstPQSTestBase
{
    const int SampleCount = 777;

    [BurstCompile]
    struct EvaluateAoSJob : IJob
    {
        public NoiseProgram program;
        public NativeArray<double3> coords;
        public NativeArray<double> output;
        public NativeArray<bool> burst;

        public void Execute()
        {
            burst[0] = BurstPQS.Util.BurstUtil.IsBurstCompiled;
            program.Evaluate(new MemorySpan<double3>(coords), new MemorySpan<double>(output));
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct FastEvaluateAoSJob : IJob
    {
        public NoiseProgram program;
        public NativeArray<double3> coords;
        public NativeArray<double> output;
        public NativeArray<bool> burst;

        public void Execute()
        {
            burst[0] = BurstPQS.Util.BurstUtil.IsBurstCompiled;
            program.Evaluate(new MemorySpan<double3>(coords), new MemorySpan<double>(output));
        }
    }

    [BurstCompile]
    struct EvaluateSoAJob : IJob
    {
        public NoiseProgram program;
        public NativeArray<double> x;
        public NativeArray<double> y;
        public NativeArray<double> z;
        public NativeArray<double> output;

        public void Execute() =>
            program.Evaluate(
                new MemorySpan<double>(x),
                new MemorySpan<double>(y),
                new MemorySpan<double>(z),
                new MemorySpan<double>(output)
            );
    }

    class OffsetModule(double offset) : IModule
    {
        public double GetValue(Vector3d c) => GetValue(c.x, c.y, c.z);

        public double GetValue(Vector3 c) => GetValue(c.x, c.y, c.z);

        public double GetValue(double x, double y, double z) => x * 0.25 - y * z + offset;
    }

    static void AssertClose(string name, string path, int i, double actual, double expected)
    {
        if (double.IsNaN(expected) && double.IsNaN(actual))
            return;

        double tol = 1e-9 * System.Math.Max(1.0, System.Math.Abs(expected));
        if (!(System.Math.Abs(actual - expected) <= tol))
            throw new Exception(
                $"TEST {name} ({path}) sample {i}: FAIL! {actual:R} != {expected:R} (tol={tol})"
            );
    }

    void Check(string name, IModule module, bool expectManaged = false, double range = 40.0)
    {
        var rng = new System.Random(name.GetHashCode());
        var coords = new NativeArray<double3>(SampleCount, Allocator.TempJob);
        var x = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var y = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var z = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var aosOut = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var fastOut = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var soaOut = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var managedOut = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var burst = new NativeArray<bool>(1, Allocator.TempJob);
        var program = NoiseProgram.Compile(module);

        try
        {
            if (program.UsesManagedFallback != expectManaged)
                throw new Exception(
                    $"TEST {name}: FAIL! UsesManagedFallback = {program.UsesManagedFallback}"
                );
            if (NoiseProgram.IsSupported(module) == expectManaged)
                throw new Exception($"TEST {name}: FAIL! IsSupported disagrees with Compile");

            var expected = new double[SampleCount];
            for (int i = 0; i < SampleCount; ++i)
            {
                var c = new double3(
                    (rng.NextDouble() * 2.0 - 1.0) * range,
                    (rng.NextDouble() * 2.0 - 1.0) * range,
                    (rng.NextDouble() * 2.0 - 1.0) * range
                );
                coords[i] = c;
                x[i] = c.x;
                y[i] = c.y;
                z[i] = c.z;
                expected[i] = module.GetValue(c.x, c.y, c.z);
            }

            new EvaluateAoSJob
            {
                program = program,
                coords = coords,
                output = aosOut,
                burst = burst,
            }
                .Schedule()
                .Complete();
            new FastEvaluateAoSJob
            {
                program = program,
                coords = coords,
                output = fastOut,
                burst = burst,
            }
                .Schedule()
                .Complete();
            new EvaluateSoAJob
            {
                program = program,
                x = x,
                y = y,
                z = z,
                output = soaOut,
            }
                .Schedule()
                .Complete();
            program.Evaluate(new MemorySpan<double3>(coords), new MemorySpan<double>(managedOut));

            if (!burst[0])
                throw new Exception($"TEST {name}: FAIL! job was not Burst-compiled");

            for (int i = 0; i < SampleCount; ++i)
            {
                AssertClose(name, "burst aos", i, aosOut[i], expected[i]);
                AssertClose(name, "burst fast", i, fastOut[i], expected[i]);
                AssertClose(name, "burst soa", i, soaOut[i], expected[i]);
                AssertClose(name, "managed", i, managedOut[i], expected[i]);
            }

            var c0 = coords[0];
            AssertClose(name, "single", 0, program.Evaluate(c0), expected[0]);
        }
        finally
        {
            program.Dispose();
            coords.Dispose();
            x.Dispose();
            y.Dispose();
            z.Dispose();
            aosOut.Dispose();
            fastOut.Dispose();
            soaOut.Dispose();
            managedOut.Dispose();
            burst.Dispose();
        }
    }

    static Perlin MakePerlin(int seed = 7) =>
        new()
        {
            Frequency = 0.37,
            Lacunarity = 2.1,
            Persistence = 0.45,
            OctaveCount = 5,
            NoiseQuality = NoiseQuality.Standard,
            Seed = seed,
        };

    static IEnumerable<NoiseQuality> Qualities =>
        [NoiseQuality.Low, NoiseQuality.Standard, NoiseQuality.High];

    #region Generators
    [TestInfo("NoiseProgram_Constant")]
    public void TestConstant() => Check("Constant", new Constant(3.25));

    [TestInfo("NoiseProgram_Perlin")]
    public void TestPerlin()
    {
        foreach (var q in Qualities)
        {
            var perlin = MakePerlin(1234);
            perlin.NoiseQuality = q;
            Check($"Perlin_{q}", perlin);
        }
    }

    [TestInfo("NoiseProgram_Billow")]
    public void TestBillow()
    {
        foreach (var q in Qualities)
        {
            Check(
                $"Billow_{q}",
                new Billow
                {
                    Frequency = 0.6,
                    Lacunarity = 1.9,
                    Persistence = 0.55,
                    OctaveCount = 7,
                    NoiseQuality = q,
                    Seed = 99,
                }
            );
        }
    }

    [TestInfo("NoiseProgram_RidgedMultifractal")]
    public void TestRidgedMultifractal()
    {
        foreach (var q in Qualities)
        {
            Check(
                $"Ridged_{q}",
                new RidgedMultifractal
                {
                    Frequency = 0.5,
                    Lacunarity = 2.3,
                    OctaveCount = 8,
                    NoiseQuality = q,
                    Seed = 42,
                }
            );
        }
    }

    [TestInfo("NoiseProgram_FastNoise")]
    public void TestFastNoise()
    {
        foreach (var q in Qualities)
        {
            Check(
                $"FastNoise_{q}",
                new FastNoise(17)
                {
                    Frequency = 0.8,
                    Lacunarity = 2.2,
                    Persistence = 0.5,
                    OctaveCount = 6,
                    NoiseQuality = q,
                }
            );
        }
    }

    [TestInfo("NoiseProgram_FastBillow")]
    public void TestFastBillow()
    {
        foreach (var q in Qualities)
        {
            Check(
                $"FastBillow_{q}",
                new FastBillow(5)
                {
                    Frequency = 0.8,
                    Lacunarity = 2.0,
                    Persistence = 0.6,
                    OctaveCount = 4,
                    NoiseQuality = q,
                }
            );
        }
    }

    [TestInfo("NoiseProgram_FastRidgedMultifractal")]
    public void TestFastRidgedMultifractal()
    {
        foreach (var q in Qualities)
        {
            Check(
                $"FastRidged_{q}",
                new FastRidgedMultifractal(23)
                {
                    Frequency = 0.4,
                    Lacunarity = 2.4,
                    OctaveCount = 9,
                    NoiseQuality = q,
                }
            );
        }
    }

    [TestInfo("NoiseProgram_Voronoi")]
    public void TestVoronoi()
    {
        Check(
            "Voronoi_Distance",
            new Voronoi
            {
                Frequency = 0.3,
                Displacement = 1.5,
                DistanceEnabled = true,
                Seed = 3,
            }
        );
        Check(
            "Voronoi_NoDistance",
            new Voronoi
            {
                Frequency = 1.1,
                Displacement = 0.5,
                DistanceEnabled = false,
                Seed = 8,
            }
        );
    }

    [TestInfo("NoiseProgram_Checkerboard")]
    public void TestCheckerboard() => Check("Checkerboard", new Checkerboard());

    [TestInfo("NoiseProgram_Cylinders")]
    public void TestCylinders() => Check("Cylinders", new Cylinders { Frequency = 0.7 });

    [TestInfo("NoiseProgram_Spheres")]
    public void TestSpheres() => Check("Spheres", new Spheres { Frequency = 0.7 });

    [TestInfo("NoiseProgram_Turbulence")]
    public void TestTurbulence() =>
        Check(
            "Turbulence",
            new Turbulence(MakePerlin())
            {
                Frequency = 0.9,
                Power = 0.75,
                Roughness = 4,
                Seed = 11,
            }
        );

    [TestInfo("NoiseProgram_FastTurbulence")]
    public void TestFastTurbulence() =>
        Check(
            "FastTurbulence",
            new FastTurbulence(MakePerlin())
            {
                Frequency = 0.9,
                Power = 1.25,
                Roughness = 3,
                Seed = 19,
            }
        );
    #endregion

    #region Modifiers
    [TestInfo("NoiseProgram_UnaryOutputs")]
    public void TestUnaryOutputs()
    {
        Check("AbsoluteOutput", new AbsoluteOutput(MakePerlin()));
        Check("BiasOutput", new BiasOutput(MakePerlin(), 0.3));
        Check("ScaleOutput", new ScaleOutput(MakePerlin(), -2.5));
        Check("ScaleBiasOutput", new ScaleBiasOutput(MakePerlin()) { Scale = 1.5, Bias = -0.2 });
        Check("ExponentialOutput", new ExponentialOutput(MakePerlin(), 2.2));
        Check("InvertOutput", new InvertOutput(MakePerlin()));

        var clamp = new ClampOutput(MakePerlin());
        clamp.SetBounds(-0.25, 0.4);
        Check("ClampOutput", clamp);
    }

    [TestInfo("NoiseProgram_Terrace")]
    public void TestTerrace()
    {
        foreach (var invert in new[] { false, true })
        {
            var terrace = new Terrace(MakePerlin()) { InvertTerraces = invert };
            terrace.ControlPoints.AddRange([-1.0, -0.3, 0.1, 0.25, 0.8]);
            Check($"Terrace_{invert}", terrace);
        }
    }

    [TestInfo("NoiseProgram_BinaryOutputs")]
    public void TestBinaryOutputs()
    {
        Check("Add", new Add(MakePerlin(1), MakePerlin(2)));
        Check("Multiply", new Multiply(MakePerlin(1), MakePerlin(2)));
        Check("LargerOutput", new LargerOutput(MakePerlin(1), MakePerlin(2)));
        Check("SmallerOutput", new SmallerOutput(MakePerlin(1), MakePerlin(2)));
        Check(
            "Power",
            new Power(new BiasOutput(new AbsoluteOutput(MakePerlin(1)), 0.1), MakePerlin(2))
        );
    }

    [TestInfo("NoiseProgram_Blend")]
    public void TestBlend() =>
        Check("Blend", new Blend(MakePerlin(1), new Constant(0.5), MakePerlin(3)));

    [TestInfo("NoiseProgram_Select")]
    public void TestSelect()
    {
        var hard = new Select(MakePerlin(1), MakePerlin(2), new Constant(-0.5));
        hard.SetBounds(-0.2, 0.3);
        Check("Select_Hard", hard);

        var soft = new Select(MakePerlin(1), MakePerlin(2), new Constant(-0.5));
        soft.SetBounds(-0.2, 0.3);
        soft.EdgeFalloff = 0.1;
        Check("Select_Falloff", soft);
    }

    [TestInfo("NoiseProgram_InputTransforms")]
    public void TestInputTransforms()
    {
        Check("TranslateInput", new TranslateInput(MakePerlin(), 1.5, -2.25, 0.75));
        // The stock ScaleInput constructor never assigns SourceModule.
        Check(
            "ScaleInput",
            new ScaleInput(MakePerlin(), 0.5, 2.0, -1.5) { SourceModule = MakePerlin() }
        );
        Check("InvertInput", new InvertInput(MakePerlin()));

        var rotate = new RotateInput(MakePerlin(), 0, 0, 0);
        rotate.SetAngles(0.3, -1.2, 2.0);
        Check("RotateInput", rotate);

        Check(
            "DisplaceInput",
            new DisplaceInput(MakePerlin(1), MakePerlin(2), MakePerlin(3), MakePerlin(4))
        );
    }
    #endregion

    #region Composite
    [TestInfo("NoiseProgram_Composite")]
    public void TestComposite()
    {
        var terrace = new Terrace(new ScaleBiasOutput(new Voronoi { Seed = 2 }) { Scale = 0.5 });
        terrace.ControlPoints.AddRange([-1.0, 0.0, 1.0]);

        var select = new Select(
            new DisplaceInput(
                new RidgedMultifractal { Seed = 4 },
                new ScaleOutput(MakePerlin(5), 0.2),
                new Turbulence(new Checkerboard()) { Seed = 6 },
                new Constant(0.1)
            ),
            terrace,
            new TranslateInput(new FastBillow(9), 0.1, 0.2, 0.3)
        );
        select.SetBounds(-0.1, 0.6);
        select.EdgeFalloff = 0.05;

        var root = new Add(
            new Blend(
                select,
                new FastTurbulence(new Spheres()) { Seed = 1 },
                new RotateInput(new Cylinders(), 0.5, 0.6, 0.7)
            ),
            new InvertInput(new ExponentialOutput(new FastRidgedMultifractal(3), 1.7))
        );

        Check("Composite", root);
    }

    [TestInfo("NoiseProgram_ManagedFallback")]
    public void TestManagedFallback()
    {
        Check("Managed_Root", new OffsetModule(0.5), expectManaged: true);
        Check(
            "Managed_Nested",
            new Add(
                new TranslateInput(new OffsetModule(-1.0), 0.5, 0.25, -0.75),
                new DisplaceInput(
                    MakePerlin(),
                    new OffsetModule(0.1),
                    new Constant(0.0),
                    MakePerlin(3)
                )
            ),
            expectManaged: true
        );
    }

    // More points than fit in one chunk, with a deep stack in every chunk.
    [TestInfo("NoiseProgram_DeepStack")]
    public void TestDeepStack()
    {
        IModule module = MakePerlin(0);
        for (int i = 1; i < 12; ++i)
            module = new Add(MakePerlin(i), new TranslateInput(module, 0.1 * i, 0, 0));
        Check("DeepStack", module);
    }
    #endregion
}
