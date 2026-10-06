using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BurstPQS.Jobs;
using BurstPQS.Util;
using HarmonyLib;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;

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

[HarmonyPatch(typeof(PQS), nameof(PQS.StartSphere))]
internal static class PQS_StartSphere_Patch
{
    static void Prefix(PQS __instance)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS != null)
            batchPQS.InvalidateActiveQuads();
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.BuildQuad))]
[HarmonyPriority(Priority.VeryLow)]
internal static class PQS_BuildQuad_Patch
{
    static bool Prefix(PQS __instance, PQ quad, ref bool __result)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS is null || batchPQS.Fallback)
            return true;

        __result = batchPQS.BuildQuad(quad);
        return false;
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.QuadCreated))]
internal static class PQS_QuadCreated_Patch
{
    static void Postfix(PQS __instance, PQ quad)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS != null)
            batchPQS.OnQuadCreated(quad);
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.DestroyQuad))]
internal static class PQS_DestroyQuad_Patch
{
    static void Prefix(PQS __instance, PQ quad)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS != null)
            batchPQS.OnQuadDestroying(quad);
    }

    static void Postfix(PQS __instance, PQ quad)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS != null)
            batchPQS.OnQuadDestroy(quad);
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.FastUpdateQuadsPosition))]
internal static class PQS_FastUpdateQuadsPosition_Patch
{
    static void Postfix(PQS __instance)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS != null)
            batchPQS.UpdateStoragePositions();
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.PreciseUpdateQuadsPosition))]
internal static class PQS_PreciseUpdateQuadsPosition_Patch
{
    static void Postfix(PQS __instance)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS != null)
            batchPQS.UpdateStoragePositions();
    }
}

[HarmonyPatch(typeof(CelestialBody), nameof(CelestialBody.CBUpdate))]
internal static class CelestialBody_CBUpdate_Patch
{
    static void Postfix(CelestialBody __instance)
    {
        var pqs = __instance.pqsController;
        if (pqs == null)
            return;

        var batchPQS = BatchPQS.Get(pqs);
        if (batchPQS != null)
            batchPQS.UpdateLocalStorage();
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.UpdateQuads))]
internal static class PQS_UpdateQuads_Patch
{
    static bool Prefix(PQS __instance)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS is null || batchPQS.Fallback)
            return true;

        batchPQS.UpdateQuads();
        return false;
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.UpdateQuadsInit))]
internal static class PQS_UpdateQuadsInit_Patch
{
    static bool Prefix(PQS __instance)
    {
        var batchPQS = BatchPQS.Get(__instance);
        if (batchPQS is null || batchPQS.Fallback)
            return true;

        batchPQS.UpdateQuadsInit();
        return false;
    }
}

[BurstCompile]
[HarmonyPatch(typeof(PQS), nameof(PQS.BuildTangents))]
internal static class PQS_BuildTangents_Patch
{
    private static readonly List<Vector3> NormalListCache = new(PQS.cacheVertCount);

    static unsafe bool Prefix(PQ quad)
    {
        BuildTangentsFunc ??= BurstUtil.MaybeCompileDelegate<BuildTangentsDelegate>(BuildTangents);

        NormalListCache.Clear();
        quad.mesh.GetNormals(NormalListCache);

        fixed (Vector3* pnormals = NoAllocHelpers.ExtractArrayFromListT(NormalListCache))
        fixed (Vector4* ptangents = PQS.cacheTangents)
        fixed (Vector3* ptan2 = PQS.tan2)
        {
            BuildTangentsFunc(
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector3>(
                    pnormals,
                    NormalListCache.Count,
                    Allocator.Invalid
                ),
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector4>(
                    ptangents,
                    PQS.cacheTangents.Length,
                    Allocator.Invalid
                ),
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector3>(
                    ptan2,
                    PQS.tan2.Length,
                    Allocator.Invalid
                )
            );
        }

        quad.mesh.tangents = PQS.cacheTangents;
        return false;
    }

    delegate void BuildTangentsDelegate(
        in NativeArray<Vector3> normals,
        in NativeArray<Vector4> tangents,
        in NativeArray<Vector3> tan2
    );

    static BuildTangentsDelegate BuildTangentsFunc;

    [BurstCompile(FloatMode = FloatMode.Fast)]
    static void BuildTangents(
        in NativeArray<Vector3> normals,
        in NativeArray<Vector4> tangents,
        in NativeArray<Vector3> tan2
    )
    {
        BuildQuadJob.BuildTangents(normals, tangents, tan2);
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.AssignQuad))]
internal static class PQS_AssignQuad_Patch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var setParent = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.parent));
        var replacement = SymbolExtensions.GetMethodInfo(() =>
            BatchPQS.AssignQuadParent(null, null, null, 0)
        );

        var matcher = new CodeMatcher(instructions);
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Callvirt, setParent))
            .ThrowIfInvalid("Could not find call to Transform.set_parent in PQS.AssignQuad")
            .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
            .Insert(
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, replacement)
            );

        return matcher.Instructions();
    }
}

[HarmonyPatch(typeof(PQS), nameof(PQS.UpdateEdgeNormals))]
internal static class PQS_UpdateEdgeNormals_Patch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var setNormals = AccessTools.PropertySetter(typeof(Mesh), nameof(Mesh.normals));
        var replacement = SymbolExtensions.GetMethodInfo(() =>
            BatchPQS.SetQuadNormals(null, null)
        );

        var matcher = new CodeMatcher(instructions);
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Callvirt, setNormals))
            .ThrowIfInvalid("Could not find call to Mesh.set_normals in PQS.UpdateEdgeNormals")
            .Repeat(m => m.SetInstruction(new CodeInstruction(OpCodes.Call, replacement)));

        return matcher.Instructions();
    }
}

[HarmonyPatch]
internal static class PQS_RevPatch
{
    [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
    [HarmonyPatch(typeof(PQS), nameof(PQS.BuildQuad))]
    public static bool BuildQuad(PQS pqs, PQ quad) => throw new NotImplementedException();
}
