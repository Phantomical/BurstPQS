using System.Runtime.CompilerServices;
using BurstPQS.Noise;
using HarmonyLib;
using Unity.Burst;

namespace BurstPQS.Patches;

// [BurstCompile]
// [HarmonyPatch(typeof(Simplex), nameof(Simplex.noise))]
// [HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static class Simplex_Noise_Patch
{
    delegate double NoiseDelegate(in BurstSimplex simplex, double x, double y, double z);

    static readonly NoiseDelegate NoiseFp = BurstCompiler
        .CompileFunctionPointer<NoiseDelegate>(Noise)
        .Invoke;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Prefix(Simplex __instance, double x, double y, double z, out double __result)
    {
        using var bsimplex = new BurstSimplex(__instance);

        __result = Noise(in bsimplex, x, y, z);
        return false;
    }

    // [BurstCompile]
    // [BurstPQSAutoPatch]
    static double Noise(in BurstSimplex simplex, double x, double y, double z)
    {
        return simplex.noise(x, y, z);
    }
}

// [BurstCompile]
// [HarmonyPatch(typeof(LibNoise.Billow), nameof(LibNoise.Billow.GetValue))]
// [HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static class Billow_GetValue_Patch
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Prefix(
        LibNoise.Billow __instance,
        double x,
        double y,
        double z,
        out double __result
    )
    {
        __result = GetValue(new(__instance), x, y, z);
        return false;
    }

    // [BurstCompile]
    [BurstPQSAutoPatch]
    static double GetValue(in BurstBillow noise, double x, double y, double z) =>
        noise.GetValue(x, y, z);
}

// [BurstCompile]
// [HarmonyPatch(typeof(LibNoise.RidgedMultifractal), "GetValue")]
// [HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static class RidgedMultifractal_GetValue_Patch
{
    static bool Prefix(
        LibNoise.RidgedMultifractal __instance,
        double x,
        double y,
        double z,
        out double __result
    )
    {
        __result = GetValue(new(__instance), x, y, z);
        return false;
    }

    // [BurstCompile]
    [BurstPQSAutoPatch]
    static double GetValue(in BurstRidgedMultifractal noise, double x, double y, double z) =>
        noise.GetValue(x, y, z);
}

// [BurstCompile]
// [HarmonyPatch(typeof(LibNoise.Voronoi), "GetValue")]
// [HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static class Voronoi_GetValue_Patch
{
    delegate double GetValueDelegate(in BurstVoronoi noise, double x, double y, double z);

    static readonly GetValueDelegate GetValueFp = BurstCompiler
        .CompileFunctionPointer<GetValueDelegate>(GetValue)
        .Invoke;

    static bool Prefix(
        LibNoise.Voronoi __instance,
        double x,
        double y,
        double z,
        out double __result
    )
    {
        __result = GetValueFp(new(__instance), x, y, z);
        return false;
    }

    // [BurstCompile]
    // [BurstPQSAutoPatch]
    static double GetValue(in BurstVoronoi noise, double x, double y, double z) =>
        noise.GetValue(x, y, z);
}
