using System;
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

[HarmonyPatch(typeof(PQS), nameof(PQS.DestroyQuad))]
internal static class PQS_DestroyQuad_Patch
{
    static void Postfix(PQS __instance, PQ quad)
    {
        var batchPQS = __instance.GetComponent<BatchPQS>();
        if (batchPQS != null)
            batchPQS.OnQuadDestroy(quad);
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.UpdateQuads))]
[HarmonyPriority(Priority.VeryLow)]
internal static class PQS_UpdateQuads_Patch
{
    static bool Prefix(PQS __instance)
    {
        var batchPQS = __instance.GetComponent<BatchPQS>();
        if (batchPQS is null)
            return true;

        batchPQS.UpdateQuads();
        return false;
    }
}

[HarmonyPatch]
[HarmonyPriority(Priority.VeryLow + 1)]
internal static class PQS_RevPatch
{
    [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
    [HarmonyPatch(typeof(PQS), nameof(PQS.BuildQuad))]
    public static bool BuildQuad(PQS pqs, PQ quad) => throw new NotImplementedException();

    [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
    [HarmonyPatch(typeof(PQS), nameof(PQS.UpdateQuadsInit))]
    public static void UpdateQuadsInit(PQS pqs) => throw new NotImplementedException();

    [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
    [HarmonyPatch(typeof(PQS), nameof(PQS.UpdateQuads))]
    public static void UpdateQuads(PQS pqs) => throw new NotImplementedException();
}
