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
