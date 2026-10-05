using BurstPQS.Util;
using LibNoise;
using Unity.Mathematics;
using UnityEngine;
using static BurstPQS.Noise.ValueNoiseBasis;

namespace BurstPQS.Noise;

/// <summary>
/// Burst-compatible voronoi noise.
/// </summary>
public readonly struct BurstVoronoi : IModule
{
    public readonly int Seed;
    public readonly bool DistanceEnabled;
    public readonly double Displacement;
    public readonly double Frequency;

    public BurstVoronoi(Voronoi voronoi)
    {
        Seed = voronoi.Seed;
        Displacement = voronoi.Displacement;
        Frequency = voronoi.Frequency;
        DistanceEnabled = voronoi.DistanceEnabled;
    }

    public double GetValue(Vector3d coordinate)
    {
        return GetValue(BurstUtil.ConvertVector(coordinate));
    }

    public double GetValue(Vector3 coordinate)
    {
        return GetValue(BurstUtil.ConvertVector((Vector3d)coordinate));
    }

    public double GetValue(double x, double y, double z)
    {
        return GetValue(new double3(x, y, z));
    }

    private double GetValue(double3 c)
    {
        double3 p = c * Frequency;
        return GetValue(p, FindNearest(p));
    }

    private double GetValue(double3 p, double3 nearest)
    {
        double value;
        if (DistanceEnabled)
        {
            value = math.distance(nearest, p) * Math.Sqrt3 - 1.0;
        }
        else
        {
            value = 0.0;
        }
        int nearestCellX = ((nearest.x > 0.0) ? ((int)nearest.x) : ((int)nearest.x - 1));
        int nearestCellY = ((nearest.y > 0.0) ? ((int)nearest.y) : ((int)nearest.y - 1));
        int nearestCellZ = ((nearest.z > 0.0) ? ((int)nearest.z) : ((int)nearest.z - 1));
        return value + Displacement * ValueNoise(nearestCellX, nearestCellY, nearestCellZ);
    }

    public Vector3d GetNearest(Vector3d candidate) =>
        BurstUtil.ConvertVector(FindNearest(BurstUtil.ConvertVector(candidate) * Frequency));

    public Vector3d GetNearest(double x, double y, double z) => GetNearest(new Vector3d(x, y, z));

    private double3 FindNearestFallback(double3 p)
    {
        int cellX = ((p.x > 0.0) ? ((int)p.x) : ((int)p.x - 1));
        int cellY = ((p.y > 0.0) ? ((int)p.y) : ((int)p.y - 1));
        int cellZ = ((p.z > 0.0) ? ((int)p.z) : ((int)p.z - 1));
        double minDist = 2147483647.0;
        double3 nearest = 0.0;
        for (int i = cellZ - 2; i <= cellZ + 2; i++)
        {
            for (int j = cellY - 2; j <= cellY + 2; j++)
            {
                for (int k = cellX - 2; k <= cellX + 2; k++)
                {
                    double pointX = (double)k + ValueNoise(k, j, i, Seed);
                    double pointY = (double)j + ValueNoise(k, j, i, Seed + 1);
                    double pointZ = (double)i + ValueNoise(k, j, i, Seed + 2);
                    double dx = pointX - p.x;
                    double dy = pointY - p.y;
                    double dz = pointZ - p.z;
                    double dist = dx * dx + dy * dy + dz * dz;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = new double3(pointX, pointY, pointZ);
                    }
                }
            }
        }
        return nearest;
    }

    // The feature point nearest to p, which is already scaled by Frequency.
    private double3 FindNearest(double3 p)
    {
        int3 cell = (int3)p - math.select(new int3(1), int3.zero, p > 0.0);
        // Beyond this the lattice coordinates can wrap around, which the
        // scalar version handles differently.
        if (math.any((uint3)(cell + (1 << 30)) > (1u << 31)))
            return FindNearestFallback(p);

        int hashBase = 1619 * cell.x + 31337 * cell.y + 6971 * cell.z + 1013 * Seed;
        // Each cell's offset from p, less the h / 2^30 jitter. The +1 is from
        // ValueNoise's 1 - h / 2^30. Folding p in here leaves one add and one
        // mad per axis in the loop.
        double3 rel = (double3)cell + 1.0 - p;
        // Broadcast up front, otherwise LLVM spills rel and re-broadcasts it
        // from the stack every iteration.
        double4 relX = rel.x;
        double4 relY = rel.y;
        double4 relZ = rel.z;

        // Visit the 125 cells 8 at a time in the same order as the scalar loop,
        // tracking each lane's nearest point by its cell index. The hash runs
        // on all 8 cells at once, the distances on two halves of 4.
        double4 bestDist = 2147483647.0;
        double4 bestIndex = -1.0;
        double4 index = new(0.0, 1.0, 2.0, 3.0);
        for (int b = 0; b < CellBatches; b += 2)
        {
            int8 h = hashBase + new int8(HashOffset[b], HashOffset[b + 1]);
            int8 hx = Hash8(h);
            int8 hy = Hash8(h + 1013);
            int8 hz = Hash8(h + 2026);

            UpdateNearest(
                hx.lo,
                hy.lo,
                hz.lo,
                b,
                relX,
                relY,
                relZ,
                index,
                ref bestDist,
                ref bestIndex
            );
            index += 4.0;
            UpdateNearest(
                hx.hi,
                hy.hi,
                hz.hi,
                b + 1,
                relX,
                relY,
                relZ,
                index,
                ref bestDist,
                ref bestIndex
            );
            index += 4.0;
        }

        // The scalar loop keeps the first of several equally near points, so
        // break ties between lanes on the cell index.
        double minDist = bestDist.x;
        double nearestIndex = bestIndex.x;
        for (int l = 1; l < 4; l++)
        {
            if (bestDist[l] < minDist || (bestDist[l] == minDist && bestIndex[l] < nearestIndex))
            {
                minDist = bestDist[l];
                nearestIndex = bestIndex[l];
            }
        }

        if (nearestIndex < 0.0)
            return 0.0;

        int n = (int)nearestIndex;
        int k = cell.x - 2 + n % 5;
        int j = cell.y - 2 + n / 5 % 5;
        int i = cell.z - 2 + n / 25;
        return new double3(
            k + ValueNoise(k, j, i, Seed),
            j + ValueNoise(k, j, i, Seed + 1),
            i + ValueNoise(k, j, i, Seed + 2)
        );
    }

    static void UpdateNearest(
        int4 hx,
        int4 hy,
        int4 hz,
        int b,
        double4 relX,
        double4 relY,
        double4 relZ,
        double4 index,
        ref double4 bestDist,
        ref double4 bestIndex
    )
    {
        double4 dx = Jitter(hx, relX + OffsetX[b]);
        double4 dy = Jitter(hy, relY + OffsetY[b]);
        double4 dz = Jitter(hz, relZ + OffsetZ[b]);
        double4 dist = dx * dx + dy * dy + dz * dz;
        bool4 closer = dist < bestDist;
        bestDist = math.select(bestDist, dist, closer);
        bestIndex = math.select(bestIndex, index, closer);
    }

    // offset - h / 2^30
    static double4 Jitter(int4 h, double4 offset) =>
        math.mad((double4)h, -1.0 / 1073741824.0, offset);

    // IntValueNoise on 8 lanes, given 1619x + 31337y + 6971z + 1013seed.
    static int8 Hash8(int8 n)
    {
        n &= 0x7FFFFFFF;
        n = (n >> 13) ^ n;
        return (n * (n * n * 60493 + 19990303) + 1376312589) & 0x7FFFFFFF;
    }

    const int CellBatches = 32;

    // Per-lane cell offsets for the 125 cells in scalar loop order, padded to 128.
    // The padding cells are offset far away so they never compare as nearer. This
    // avoids NaN, which fast math assumes never happens.
    // csharpier-ignore
    static readonly int4[] HashOffset =
    [
        new(-79854, -78235, -76616, -74997), new(-73378, -48517, -46898, -45279), new(-43660, -42041, -17180, -15561), new(-13942, -12323, -10704, 14157),
        new(15776, 17395, 19014, 20633), new(45494, 47113, 48732, 50351), new(51970, -72883, -71264, -69645), new(-68026, -66407, -41546, -39927),
        new(-38308, -36689, -35070, -10209), new(-8590, -6971, -5352, -3733), new(21128, 22747, 24366, 25985), new(27604, 52465, 54084, 55703),
        new(57322, 58941, -65912, -64293), new(-62674, -61055, -59436, -34575), new(-32956, -31337, -29718, -28099), new(-3238, -1619, 0, 1619),
        new(3238, 28099, 29718, 31337), new(32956, 34575, 59436, 61055), new(62674, 64293, 65912, -58941), new(-57322, -55703, -54084, -52465),
        new(-27604, -25985, -24366, -22747), new(-21128, 3733, 5352, 6971), new(8590, 10209, 35070, 36689), new(38308, 39927, 41546, 66407),
        new(68026, 69645, 71264, 72883), new(-51970, -50351, -48732, -47113), new(-45494, -20633, -19014, -17395), new(-15776, -14157, 10704, 12323),
        new(13942, 15561, 17180, 42041), new(43660, 45279, 46898, 48517), new(73378, 74997, 76616, 78235), new(79854, 0, 0, 0),
    ];

    // csharpier-ignore
    static readonly double4[] OffsetX =
    [
        new(-2.0, -1.0, 0.0, 1.0), new(2.0, -2.0, -1.0, 0.0), new(1.0, 2.0, -2.0, -1.0), new(0.0, 1.0, 2.0, -2.0),
        new(-1.0, 0.0, 1.0, 2.0), new(-2.0, -1.0, 0.0, 1.0), new(2.0, -2.0, -1.0, 0.0), new(1.0, 2.0, -2.0, -1.0),
        new(0.0, 1.0, 2.0, -2.0), new(-1.0, 0.0, 1.0, 2.0), new(-2.0, -1.0, 0.0, 1.0), new(2.0, -2.0, -1.0, 0.0),
        new(1.0, 2.0, -2.0, -1.0), new(0.0, 1.0, 2.0, -2.0), new(-1.0, 0.0, 1.0, 2.0), new(-2.0, -1.0, 0.0, 1.0),
        new(2.0, -2.0, -1.0, 0.0), new(1.0, 2.0, -2.0, -1.0), new(0.0, 1.0, 2.0, -2.0), new(-1.0, 0.0, 1.0, 2.0),
        new(-2.0, -1.0, 0.0, 1.0), new(2.0, -2.0, -1.0, 0.0), new(1.0, 2.0, -2.0, -1.0), new(0.0, 1.0, 2.0, -2.0),
        new(-1.0, 0.0, 1.0, 2.0), new(-2.0, -1.0, 0.0, 1.0), new(2.0, -2.0, -1.0, 0.0), new(1.0, 2.0, -2.0, -1.0),
        new(0.0, 1.0, 2.0, -2.0), new(-1.0, 0.0, 1.0, 2.0), new(-2.0, -1.0, 0.0, 1.0), new(2.0, 1.0e6, 1.0e6, 1.0e6),
    ];

    // csharpier-ignore
    static readonly double4[] OffsetY =
    [
        new(-2.0, -2.0, -2.0, -2.0), new(-2.0, -1.0, -1.0, -1.0), new(-1.0, -1.0, 0.0, 0.0), new(0.0, 0.0, 0.0, 1.0),
        new(1.0, 1.0, 1.0, 1.0), new(2.0, 2.0, 2.0, 2.0), new(2.0, -2.0, -2.0, -2.0), new(-2.0, -2.0, -1.0, -1.0),
        new(-1.0, -1.0, -1.0, 0.0), new(0.0, 0.0, 0.0, 0.0), new(1.0, 1.0, 1.0, 1.0), new(1.0, 2.0, 2.0, 2.0),
        new(2.0, 2.0, -2.0, -2.0), new(-2.0, -2.0, -2.0, -1.0), new(-1.0, -1.0, -1.0, -1.0), new(0.0, 0.0, 0.0, 0.0),
        new(0.0, 1.0, 1.0, 1.0), new(1.0, 1.0, 2.0, 2.0), new(2.0, 2.0, 2.0, -2.0), new(-2.0, -2.0, -2.0, -2.0),
        new(-1.0, -1.0, -1.0, -1.0), new(-1.0, 0.0, 0.0, 0.0), new(0.0, 0.0, 1.0, 1.0), new(1.0, 1.0, 1.0, 2.0),
        new(2.0, 2.0, 2.0, 2.0), new(-2.0, -2.0, -2.0, -2.0), new(-2.0, -1.0, -1.0, -1.0), new(-1.0, -1.0, 0.0, 0.0),
        new(0.0, 0.0, 0.0, 1.0), new(1.0, 1.0, 1.0, 1.0), new(2.0, 2.0, 2.0, 2.0), new(2.0, 0.0, 0.0, 0.0),
    ];

    // csharpier-ignore
    static readonly double4[] OffsetZ =
    [
        new(-2.0, -2.0, -2.0, -2.0), new(-2.0, -2.0, -2.0, -2.0), new(-2.0, -2.0, -2.0, -2.0), new(-2.0, -2.0, -2.0, -2.0),
        new(-2.0, -2.0, -2.0, -2.0), new(-2.0, -2.0, -2.0, -2.0), new(-2.0, -1.0, -1.0, -1.0), new(-1.0, -1.0, -1.0, -1.0),
        new(-1.0, -1.0, -1.0, -1.0), new(-1.0, -1.0, -1.0, -1.0), new(-1.0, -1.0, -1.0, -1.0), new(-1.0, -1.0, -1.0, -1.0),
        new(-1.0, -1.0, 0.0, 0.0), new(0.0, 0.0, 0.0, 0.0), new(0.0, 0.0, 0.0, 0.0), new(0.0, 0.0, 0.0, 0.0),
        new(0.0, 0.0, 0.0, 0.0), new(0.0, 0.0, 0.0, 0.0), new(0.0, 0.0, 0.0, 1.0), new(1.0, 1.0, 1.0, 1.0),
        new(1.0, 1.0, 1.0, 1.0), new(1.0, 1.0, 1.0, 1.0), new(1.0, 1.0, 1.0, 1.0), new(1.0, 1.0, 1.0, 1.0),
        new(1.0, 1.0, 1.0, 1.0), new(2.0, 2.0, 2.0, 2.0), new(2.0, 2.0, 2.0, 2.0), new(2.0, 2.0, 2.0, 2.0),
        new(2.0, 2.0, 2.0, 2.0), new(2.0, 2.0, 2.0, 2.0), new(2.0, 2.0, 2.0, 2.0), new(2.0, 0.0, 0.0, 0.0),
    ];
}
