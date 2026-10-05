using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BurstPQS.Util;
using HarmonyLib;
using UnityEngine;

namespace BurstPQS.Patches;

// Child quads get moved into or out of local space storage depending on
// whether the body is currently rotating. See BatchPQS.UpdateLocalStorage.
//
// Stock computes the root quads' plane rotation with a float FromToRotation,
// which is not quite unit length. Every child quad's plane position is then
// computed by rotating with it in double precision, which skews each cube face
// slightly differently and leaves gaps of a few cm along the face edges.
[HarmonyPatch(typeof(PQ), nameof(PQ.SetupQuad))]
internal static class PQ_SetupQuad_Patch
{
    static void Postfix(PQ __instance)
    {
        if (__instance.quadRoot != null)
        {
            var batchPQS = BatchPQS.Get(__instance.sphereRoot);
            if (batchPQS != null)
                batchPQS.PlaceQuad(__instance);
            return;
        }

        var q = __instance.planeRotation;
        var norm = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        __instance.planeRotation = new QuaternionD(q.x / norm, q.y / norm, q.z / norm, q.w / norm);
    }
}

// Cached child quads are reused by Subdivide without calling SetupQuad, so
// they need to be placed here instead.
[HarmonyPatch(typeof(PQ), nameof(PQ.Subdivide))]
internal static class PQ_Subdivide_Patch
{
    static void Postfix(PQ __instance)
    {
        if (!__instance.isSubdivided)
            return;

        BatchPQS batchPQS = null;
        foreach (var child in __instance.subNodes)
        {
            if (child == null || !child.isCached)
                continue;

            batchPQS ??= BatchPQS.Get(__instance.sphereRoot);
            if (batchPQS == null)
                return;

            batchPQS.PlaceQuad(child);
        }
    }
}

[HarmonyPatch]
internal static class PQ_BuildDeferred_Patch
{
    static IEnumerable<MethodInfo> TargetMethods()
    {
        yield return SymbolExtensions.GetMethodInfo<PQ>(pq => pq.SetVisible());
        yield return SymbolExtensions.GetMethodInfo<PQ>(pq => pq.GetRightmostCornerPQ(null));
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var build = SymbolExtensions.GetMethodInfo<PQ>(pq => pq.Build());
        var replacement = SymbolExtensions.GetMethodInfo<PQ>(pq => BuildDeferred(pq));

        var matcher = new CodeMatcher(instructions);
        matcher
            .MatchStartForward(
                new CodeMatch(inst =>
                {
                    if (inst.opcode != OpCodes.Call && inst.opcode != OpCodes.Callvirt)
                        return false;

                    if (inst.operand is not MethodInfo method)
                        return false;

                    return method == build;
                })
            )
            .Repeat(inst => inst.Set(OpCodes.Call, replacement));

        return matcher.Instructions();
    }

    static void BuildDeferred(PQ quad)
    {
        var batchPQS = BatchPQS.Get(quad.sphereRoot);
        if (batchPQS.IsNullOrDestroyed())
            quad.Build();
        else
            batchPQS.BuildDeferred(quad);
    }
}
