using BurstPQS.Noise;
using HarmonyLib;
using Unity.Burst;

namespace BurstPQS.Patches;

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
