using System;
using System.Diagnostics;
using System.Text;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using LibNoise;
using LibNoise.Modifiers;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BurstPQS.Test.Noise;

/// <summary>
/// Compares <see cref="NoiseProgram"/> against the per-point Burst noise
/// structs and against managed LibNoise, evaluating one quad's worth of
/// vertices per call the way the noise mods do.
/// </summary>
/// <remarks>
/// Run it from the in-game console with <c>NoiseBenchmark.Run()</c>. The
/// results are logged and returned.
/// </remarks>
public static class NoiseBenchmark
{
    const int BurstIterations = 2000;
    const int ManagedIterations = 100;

    [BurstCompile]
    struct ProgramJob : IJob
    {
        public NoiseProgram program;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            var span = new MemorySpan<Vector3d>(coords);
            double total = 0.0;
            for (int it = 0; it < iterations; ++it)
            {
                using var values = program.Evaluate(span, Allocator.Temp);
                total += values[it % values.Length];
            }
            sink[0] = total;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct FastProgramJob : IJob
    {
        public NoiseProgram program;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            var span = new MemorySpan<Vector3d>(coords);
            double total = 0.0;
            for (int it = 0; it < iterations; ++it)
            {
                using var values = program.Evaluate(span, Allocator.Temp);
                total += values[it % values.Length];
            }
            sink[0] = total;
        }
    }

    static double RunScalar<N>(in N noise, NativeArray<Vector3d> coords, int iterations)
        where N : IModule
    {
        double total = 0.0;
        for (int it = 0; it < iterations; ++it)
        {
            using var values = new NativeArray<double>(
                coords.Length,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            var span = new MemorySpan<double>(values);
            for (int i = 0; i < coords.Length; ++i)
                span[i] = noise.GetValue(coords[i]);
            total += span[it % span.Length];
        }
        return total;
    }

    [BurstCompile]
    struct ScalarPerlinJob : IJob
    {
        public BurstPerlin noise;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            sink[0] = RunScalar(in noise, coords, iterations);
        }
    }

    [BurstCompile]
    struct ScalarBillowJob : IJob
    {
        public BurstBillow noise;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            sink[0] = RunScalar(in noise, coords, iterations);
        }
    }

    [BurstCompile]
    struct ScalarRidgedJob : IJob
    {
        public BurstRidgedMultifractal noise;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            sink[0] = RunScalar(in noise, coords, iterations);
        }
    }

    [BurstCompile]
    struct ScalarVoronoiJob : IJob
    {
        public BurstVoronoi noise;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            sink[0] = RunScalar(in noise, coords, iterations);
        }
    }

    [BurstCompile]
    struct ScalarSimplexJob : IJob
    {
        public BurstSimplex simplex;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            double total = 0.0;
            for (int it = 0; it < iterations; ++it)
            {
                using var values = new NativeArray<double>(
                    coords.Length,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory
                );
                var span = new MemorySpan<double>(values);
                for (int i = 0; i < coords.Length; ++i)
                    span[i] = simplex.noise(coords[i]);
                total += span[it % span.Length];
            }
            sink[0] = total;
        }
    }

    [BurstCompile]
    struct BatchSimplexJob : IJob
    {
        public BurstSimplex simplex;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            var span = new MemorySpan<Vector3d>(coords);
            double total = 0.0;
            for (int it = 0; it < iterations; ++it)
            {
                using var values = simplex.GetValues(span, Allocator.Temp);
                total += values[it % values.Length];
            }
            sink[0] = total;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct FastBatchSimplexJob : IJob
    {
        public BurstSimplex simplex;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> sink;
        public NativeArray<bool> burst;
        public int iterations;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            var span = new MemorySpan<Vector3d>(coords);
            double total = 0.0;
            for (int it = 0; it < iterations; ++it)
            {
                using var values = simplex.GetValues(span, Allocator.Temp);
                total += values[it % values.Length];
            }
            sink[0] = total;
        }
    }

    public static string Run()
    {
        int count = PQS.cacheVertCount;
        var rng = new System.Random(1234);
        var coords = new NativeArray<Vector3d>(count, Allocator.TempJob);
        var sink = new NativeArray<double>(1, Allocator.TempJob);
        var burst = new NativeArray<bool>(1, Allocator.TempJob);
        var sb = new StringBuilder();

        try
        {
            for (int i = 0; i < count; ++i)
            {
                var v = new Vector3d(
                    rng.NextDouble() * 2.0 - 1.0,
                    rng.NextDouble() * 2.0 - 1.0,
                    rng.NextDouble() * 2.0 - 1.0
                );
                coords[i] = v.normalized;
            }

            sb.AppendLine(
                $"NoiseProgram benchmark: {count} points per call, ns per point "
                    + $"({BurstIterations} burst / {ManagedIterations} managed calls)"
            );
            sb.AppendLine(
                $"{"case", -22} {"managed", 10} {"scalar", 10} {"batch", 10} {"fast", 10} {"vs scalar", 10} {"vs managed", 11}"
            );

            var perlin = new Perlin
            {
                Frequency = 4.0,
                Lacunarity = 2.5,
                Persistence = 0.5,
                OctaveCount = 8,
                NoiseQuality = NoiseQuality.High,
                Seed = 3,
            };
            var billow = new Billow
            {
                Frequency = 4.0,
                Lacunarity = 2.5,
                Persistence = 0.5,
                OctaveCount = 8,
                NoiseQuality = NoiseQuality.High,
                Seed = 3,
            };
            var ridged = new RidgedMultifractal
            {
                Frequency = 4.0,
                Lacunarity = 2.5,
                OctaveCount = 8,
                NoiseQuality = NoiseQuality.High,
                Seed = 3,
            };
            var voronoi = new Voronoi
            {
                Frequency = 8.0,
                Displacement = 1.0,
                DistanceEnabled = true,
                Seed = 3,
            };
            var fast = new FastNoise(3)
            {
                Frequency = 4.0,
                Lacunarity = 2.5,
                Persistence = 0.5,
                OctaveCount = 8,
                NoiseQuality = NoiseQuality.High,
            };

            var select = new Select(new Constant(0.0), new ScaleOutput(ridged, 0.5), perlin);
            select.SetBounds(-0.2, 0.4);
            select.EdgeFalloff = 0.1;
            var composite = new Add(
                new ScaleBiasOutput(new Turbulence(select) { Frequency = 2.0, Power = 0.1 })
                {
                    Scale = 0.75,
                    Bias = 0.1,
                },
                new ScaleOutput(new FastBillow(5) { Frequency = 16.0, OctaveCount = 4 }, 0.1)
            );

            Bench(
                sb,
                "Perlin",
                perlin,
                coords,
                sink,
                burst,
                () =>
                    new ScalarPerlinJob
                    {
                        noise = new(perlin),
                        coords = coords,
                        sink = sink,
                        burst = burst,
                        iterations = BurstIterations,
                    }.Schedule()
            );
            Bench(
                sb,
                "Billow",
                billow,
                coords,
                sink,
                burst,
                () =>
                    new ScalarBillowJob
                    {
                        noise = new(billow),
                        coords = coords,
                        sink = sink,
                        burst = burst,
                        iterations = BurstIterations,
                    }.Schedule()
            );
            Bench(
                sb,
                "RidgedMultifractal",
                ridged,
                coords,
                sink,
                burst,
                () =>
                    new ScalarRidgedJob
                    {
                        noise = new(ridged),
                        coords = coords,
                        sink = sink,
                        burst = burst,
                        iterations = BurstIterations,
                    }.Schedule()
            );
            Bench(
                sb,
                "Voronoi",
                voronoi,
                coords,
                sink,
                burst,
                () =>
                    new ScalarVoronoiJob
                    {
                        noise = new(voronoi),
                        coords = coords,
                        sink = sink,
                        burst = burst,
                        iterations = BurstIterations,
                    }.Schedule()
            );
            Bench(sb, "FastNoise", fast, coords, sink, burst, null);
            Bench(sb, "Composite", composite, coords, sink, burst, null);
            BenchSimplex(sb, new Simplex(3, 8, 0.5, 4.0), coords, sink, burst);
        }
        finally
        {
            coords.Dispose();
            sink.Dispose();
            burst.Dispose();
        }

        var result = sb.ToString();
        Debug.Log("[BurstPQS] " + result);
        return result;
    }

    static void BenchSimplex(
        StringBuilder sb,
        Simplex simplex,
        NativeArray<Vector3d> coords,
        NativeArray<double> sink,
        NativeArray<bool> burst
    )
    {
        int count = coords.Length;
        var values = new double[count];
        var watch = Stopwatch.StartNew();
        for (int it = 0; it < ManagedIterations; ++it)
        {
            for (int i = 0; i < count; ++i)
                values[i] = simplex.noise(coords[i]);
        }
        watch.Stop();
        double managed =
            watch.Elapsed.TotalMilliseconds * 1e6 / ((double)ManagedIterations * count);

        var burstSimplex = new BurstSimplex(simplex);
        double scalar;
        double batch;
        double fast;
        try
        {
            scalar =
                TimeJob(
                    () =>
                        new ScalarSimplexJob
                        {
                            simplex = burstSimplex,
                            coords = coords,
                            sink = sink,
                            burst = burst,
                            iterations = BurstIterations,
                        }.Schedule(),
                    burst,
                    "Simplex"
                ) / ((double)BurstIterations * count);
            batch =
                TimeJob(
                    () =>
                        new BatchSimplexJob
                        {
                            simplex = burstSimplex,
                            coords = coords,
                            sink = sink,
                            burst = burst,
                            iterations = BurstIterations,
                        }.Schedule(),
                    burst,
                    "Simplex"
                ) / ((double)BurstIterations * count);
            fast =
                TimeJob(
                    () =>
                        new FastBatchSimplexJob
                        {
                            simplex = burstSimplex,
                            coords = coords,
                            sink = sink,
                            burst = burst,
                            iterations = BurstIterations,
                        }.Schedule(),
                    burst,
                    "Simplex"
                ) / ((double)BurstIterations * count);
        }
        finally
        {
            burstSimplex.Dispose();
        }

        sb.AppendLine(
            $"{"Simplex", -22} {managed, 10:F1} {scalar, 10:F1} {batch, 10:F1} {fast, 10:F1} {(scalar / batch).ToString("F2") + "x", 10} {(managed / batch).ToString("F2") + "x", 11}"
        );
    }

    static void Bench(
        StringBuilder sb,
        string name,
        IModule module,
        NativeArray<Vector3d> coords,
        NativeArray<double> sink,
        NativeArray<bool> burst,
        Func<JobHandle> scalar
    )
    {
        int count = coords.Length;
        double managed = TimeManaged(module, coords) / ((double)ManagedIterations * count);

        double scalarNs = double.NaN;
        if (scalar is not null)
            scalarNs = TimeJob(scalar, burst, name) / ((double)BurstIterations * count);

        var program = NoiseProgram.Compile(module);
        double programNs;
        double fastNs;
        try
        {
            programNs =
                TimeJob(
                    () =>
                        new ProgramJob
                        {
                            program = program,
                            coords = coords,
                            sink = sink,
                            burst = burst,
                            iterations = BurstIterations,
                        }.Schedule(),
                    burst,
                    name
                ) / ((double)BurstIterations * count);
            fastNs =
                TimeJob(
                    () =>
                        new FastProgramJob
                        {
                            program = program,
                            coords = coords,
                            sink = sink,
                            burst = burst,
                            iterations = BurstIterations,
                        }.Schedule(),
                    burst,
                    name
                ) / ((double)BurstIterations * count);
        }
        finally
        {
            program.Dispose();
        }

        string scalarText = double.IsNaN(scalarNs) ? "-" : scalarNs.ToString("F1");
        string vsScalar = double.IsNaN(scalarNs)
            ? "-"
            : (scalarNs / programNs).ToString("F2") + "x";
        sb.AppendLine(
            $"{name, -22} {managed, 10:F1} {scalarText, 10} {programNs, 10:F1} {fastNs, 10:F1} {vsScalar, 10} {(managed / programNs).ToString("F2") + "x", 11}"
        );
    }

    // Returns the best of a few runs in nanoseconds, after a warmup run.
    static double TimeJob(Func<JobHandle> schedule, NativeArray<bool> burst, string name)
    {
        schedule().Complete();
        if (!burst[0])
            throw new Exception($"{name}: benchmark job was not Burst-compiled");

        double best = double.MaxValue;
        for (int run = 0; run < 3; ++run)
        {
            var watch = Stopwatch.StartNew();
            schedule().Complete();
            watch.Stop();
            best = System.Math.Min(best, watch.Elapsed.TotalMilliseconds * 1e6);
        }
        return best;
    }

    static double TimeManaged(IModule module, NativeArray<Vector3d> coords)
    {
        var values = new double[coords.Length];
        var watch = Stopwatch.StartNew();
        for (int it = 0; it < ManagedIterations; ++it)
        {
            for (int i = 0; i < coords.Length; ++i)
                values[i] = module.GetValue(coords[i]);
        }
        watch.Stop();
        return watch.Elapsed.TotalMilliseconds * 1e6;
    }
}
