using System;
using BurstPQS.Collections;
using BurstPQS.Noise;
using BurstPQS.Util;
using KSP.Testing;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace BurstPQS.Test.Noise;

/// <summary>
/// Validates the batch path of <see cref="BurstSimplex"/> against KSP's
/// <see cref="Simplex"/>.
/// </summary>
public class BurstSimplexTests : BurstPQSTestBase
{
    const int SampleCount = 777;

    [BurstCompile]
    struct BatchJob : IJob
    {
        public BurstSimplex simplex;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> output;
        public NativeArray<bool> burst;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            simplex.GetValues(new MemorySpan<Vector3d>(coords), new MemorySpan<double>(output));
        }
    }

    [BurstCompile]
    struct NormalizedJob : IJob
    {
        public BurstSimplex simplex;
        public NativeArray<Vector3d> coords;
        public NativeArray<double> persistence;
        public NativeArray<double> output;
        public NativeArray<double> perPointOutput;
        public NativeArray<bool> burst;

        public void Execute()
        {
            burst[0] = BurstUtil.IsBurstCompiled;
            var span = new MemorySpan<Vector3d>(coords);
            simplex.GetValuesNormalized(span, new MemorySpan<double>(output));
            simplex.GetValuesNormalized(
                span,
                new MemorySpan<double>(persistence),
                new MemorySpan<double>(perPointOutput)
            );
        }
    }

    static void AssertClose(string name, int i, double actual, double expected)
    {
        double tol = 1e-9 * Math.Max(1.0, Math.Abs(expected));
        if (!(Math.Abs(actual - expected) <= tol))
            throw new Exception(
                $"TEST {name} sample {i}: FAIL! {actual:R} != {expected:R} (tol={tol})"
            );
    }

    void CheckNormalized(string name, Simplex simplex, double range)
    {
        var rng = new Random(name.GetHashCode());
        var coords = new NativeArray<Vector3d>(SampleCount, Allocator.TempJob);
        var persistence = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var output = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var perPointOutput = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var burst = new NativeArray<bool>(1, Allocator.TempJob);
        var burstSimplex = new BurstSimplex(simplex);
        double basePersistence = simplex.persistence;

        try
        {
            for (int i = 0; i < SampleCount; ++i)
            {
                coords[i] = new Vector3d(
                    (rng.NextDouble() * 2.0 - 1.0) * range,
                    (rng.NextDouble() * 2.0 - 1.0) * range,
                    (rng.NextDouble() * 2.0 - 1.0) * range
                );
                persistence[i] = 0.2 + rng.NextDouble() * 0.6;
            }

            new NormalizedJob
            {
                simplex = burstSimplex,
                coords = coords,
                persistence = persistence,
                output = output,
                perPointOutput = perPointOutput,
                burst = burst,
            }
                .Schedule()
                .Complete();

            if (!burst[0])
                throw new Exception($"TEST {name}: FAIL! job was not Burst-compiled");

            for (int i = 0; i < SampleCount; ++i)
            {
                simplex.persistence = basePersistence;
                AssertClose(name, i, output[i], simplex.noiseNormalized(coords[i]));

                simplex.persistence = persistence[i];
                AssertClose(
                    name + " (per-point persistence)",
                    i,
                    perPointOutput[i],
                    simplex.noiseNormalized(coords[i])
                );
            }
        }
        finally
        {
            simplex.persistence = basePersistence;
            burstSimplex.Dispose();
            coords.Dispose();
            persistence.Dispose();
            output.Dispose();
            perPointOutput.Dispose();
            burst.Dispose();
        }
    }

    void Check(string name, Simplex simplex, double range)
    {
        var rng = new Random(name.GetHashCode());
        var coords = new NativeArray<Vector3d>(SampleCount, Allocator.TempJob);
        var output = new NativeArray<double>(SampleCount, Allocator.TempJob);
        var burst = new NativeArray<bool>(1, Allocator.TempJob);
        var burstSimplex = new BurstSimplex(simplex);

        try
        {
            for (int i = 0; i < SampleCount; ++i)
            {
                coords[i] = new Vector3d(
                    (rng.NextDouble() * 2.0 - 1.0) * range,
                    (rng.NextDouble() * 2.0 - 1.0) * range,
                    (rng.NextDouble() * 2.0 - 1.0) * range
                );
            }

            new BatchJob
            {
                simplex = burstSimplex,
                coords = coords,
                output = output,
                burst = burst,
            }
                .Schedule()
                .Complete();

            if (!burst[0])
                throw new Exception($"TEST {name}: FAIL! job was not Burst-compiled");

            for (int i = 0; i < SampleCount; ++i)
            {
                double expected = simplex.noise(coords[i]);
                double actual = output[i];
                double tol = 1e-9 * Math.Max(1.0, Math.Abs(expected));
                if (!(Math.Abs(actual - expected) <= tol))
                    throw new Exception(
                        $"TEST {name} sample {i}: FAIL! {actual:R} != {expected:R} (tol={tol})"
                    );
            }
        }
        finally
        {
            burstSimplex.Dispose();
            coords.Dispose();
            output.Dispose();
            burst.Dispose();
        }
    }

    [TestInfo("BurstSimplex_Batch")]
    public void TestBatch()
    {
        Check("UnitSphere", new Simplex(1234, 6, 0.5, 4.0), 1.0);
        Check("Large", new Simplex(42, 8, 0.6, 1.3), 500.0);
        Check("Negative", new Simplex(-7, 3, 0.45, 0.25), 50.0);
        Check("FractionalOctaves", new Simplex(99, 4.5, 0.5, 2.0), 10.0);
    }

    [TestInfo("BurstSimplex_Normalized")]
    public void TestNormalized()
    {
        CheckNormalized("UnitSphere", new Simplex(1234, 6, 0.5, 4.0), 1.0);
        CheckNormalized("Large", new Simplex(42, 8, 0.6, 1.3), 500.0);
    }
}
