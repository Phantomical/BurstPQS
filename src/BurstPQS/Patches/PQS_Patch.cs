using System;
using System.Security.Cryptography;
using HarmonyLib;
using Unity.Profiling;

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

[HarmonyPatch(typeof(PQS), nameof(PQS.UpdateQuadsInit))]
internal static class PQS_UpdateQuadsInit_Patch
{
    static readonly ProfilerMarker Marker = new(nameof(PQS.UpdateQuadsInit));

    static void Prefix(out ProfilerMarker.AutoScope __state) => __state = Marker.Auto();

    static void Postfix(ProfilerMarker.AutoScope __state) => __state.Dispose();
}

[HarmonyPatch]
internal static class PQS_RevPatch
{
    [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
    [HarmonyPatch(typeof(PQS), nameof(PQS.BuildQuad))]
    public static bool BuildQuad(PQS pqs, PQ quad) => throw new NotImplementedException();
}
