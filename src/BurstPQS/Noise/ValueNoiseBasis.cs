namespace BurstPQS.Noise;

public static class ValueNoiseBasis
{
    public static int IntValueNoise(int x, int y, int z, int seed)
    {
        int num = (1619 * x + 31337 * y + 6971 * z + 1013 * seed) & 0x7FFFFFFF;
        num = (num >> 13) ^ num;
        return (num * (num * num * 60493 + 19990303) + 1376312589) & 0x7FFFFFFF;
    }

    public static double ValueNoise(int x, int y, int z)
    {
        return ValueNoise(x, y, z, 0);
    }

    public static double ValueNoise(int x, int y, int z, int seed)
    {
        return 1.0 - (double)IntValueNoise(x, y, z, seed) / 1073741824.0;
    }
}
