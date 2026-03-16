using BurstPQS.Noise;
using BurstPQS.Util;
using HarmonyLib;
using Unity.Burst;

namespace BurstPQS.Patches;

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
