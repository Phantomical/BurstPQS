using HarmonyLib;

namespace BurstPQS.Patches;

[HarmonyPatch(typeof(PQS), nameof(PQS.SetupMods))]
[HarmonyPriority(Priority.VeryLow)]
internal static class PQS_SetupMods_Patch
{
    static void Postfix(PQS __instance)
    {
        if (__instance is BatchPQS pqs)
            pqs.PostSetupMods();
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.BuildQuad))]
[HarmonyPriority(Priority.VeryLow)]
internal static class PQS_BuildQuad_Patch
{
    static bool Prefix(PQS __instance, PQ quad, ref bool __result)
    {
        if (__instance is not BatchPQS pqs)
            return true;

        __result = pqs.BuildQuad(quad);
        return false;
    }
}
