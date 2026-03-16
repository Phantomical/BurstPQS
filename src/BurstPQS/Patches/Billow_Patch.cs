using System.Runtime.CompilerServices;
using BurstPQS.Noise;
using BurstPQS.Util;
using HarmonyLib;
using Unity.Burst;

namespace BurstPQS.Patches;

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
