using HarmonyLib;

namespace BurstPQS.Patches;

[HarmonyPatch(typeof(PQS), nameof(PQS.SetupMods))]
[HarmonyPriority(Priority.VeryLow)]
internal static class PQS_SetupMods_Patch
{
    static void Postfix(PQS __instance)
    {
        var batchPQS = __instance.gameObject.AddOrGetComponent<BatchPQS>();
        batchPQS.PostSetupMods();
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.BuildQuad))]
[HarmonyPriority(Priority.VeryLow)]
internal static class PQS_BuildQuad_Patch
{
    static bool Prefix(PQS __instance, PQ quad, ref bool __result)
    {
        var batchPQS = __instance.GetComponent<BatchPQS>();
        if (batchPQS is null)
            return true;

        __result = batchPQS.BuildQuad(quad);
        return false;
    }
}
