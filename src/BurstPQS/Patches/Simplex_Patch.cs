using System.Runtime.CompilerServices;
using BurstPQS.Noise;
using BurstPQS.Util;
using HarmonyLib;
using Unity.Burst;

namespace BurstPQS.Patches;

[BurstCompile]
[HarmonyPatch(typeof(Simplex), nameof(Simplex.noise))]
[HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static unsafe class Simplex_Noise_Patch
{
    delegate double NoiseDelegate(BurstSimplex* simplex, double x, double y, double z);

    static readonly NoiseDelegate NoiseFp = BurstUtil.MaybeCompileDelegate<NoiseDelegate>(Noise);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Prefix(Simplex __instance, double x, double y, double z, out double __result)
    {
        using var bsimplex = new BurstSimplex(__instance);

        __result = NoiseFp(&bsimplex, x, y, z);
        return false;
    }

    [BurstCompile]
    static double Noise(BurstSimplex* simplex, double x, double y, double z) =>
        simplex->noise(x, y, z);
}

[BurstCompile]
[HarmonyPatch(typeof(LibNoise.Billow), nameof(LibNoise.Billow.GetValue))]
[HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static unsafe class Billow_GetValue_Patch
{
    delegate double NoiseDelegate(BurstBillow* noise, double x, double y, double z);

    static readonly NoiseDelegate GetValueFp = BurstUtil.MaybeCompileDelegate<NoiseDelegate>(
        GetValue
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Prefix(
        LibNoise.Billow __instance,
        double x,
        double y,
        double z,
        out double __result
    )
    {
        var noise = new BurstBillow(__instance);
        __result = GetValueFp(&noise, x, y, z);
        return false;
    }

    [BurstCompile]
    static double GetValue(BurstBillow* noise, double x, double y, double z) =>
        noise->GetValue(x, y, z);
}

[BurstCompile]
[HarmonyPatch(typeof(LibNoise.RidgedMultifractal), "GetValue")]
[HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static unsafe class RidgedMultifractal_GetValue_Patch
{
    delegate double NoiseDelegate(BurstRidgedMultifractal* noise, double x, double y, double z);

    static readonly NoiseDelegate GetValueFp = BurstUtil.MaybeCompileDelegate<NoiseDelegate>(
        GetValue
    );

    static bool Prefix(
        LibNoise.RidgedMultifractal __instance,
        double x,
        double y,
        double z,
        out double __result
    )
    {
        var noise = new BurstRidgedMultifractal(__instance);
        __result = GetValueFp(&noise, x, y, z);
        return false;
    }

    [BurstCompile]
    static double GetValue(BurstRidgedMultifractal* noise, double x, double y, double z) =>
        noise->GetValue(x, y, z);
}

[BurstCompile]
[HarmonyPatch(typeof(LibNoise.Voronoi), "GetValue")]
[HarmonyPatch([typeof(double), typeof(double), typeof(double)])]
internal static unsafe class Voronoi_GetValue_Patch
{
    delegate double GetValueDelegate(BurstVoronoi* noise, double x, double y, double z);

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
        var noise = new BurstVoronoi(__instance);
        __result = GetValueFp(&noise, x, y, z);
        return false;
    }

    [BurstCompile]
    static double GetValue(BurstVoronoi* noise, double x, double y, double z) =>
        noise->GetValue(x, y, z);
}
