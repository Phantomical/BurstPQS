using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace BurstPQS.Patches;

[HarmonyPatch(typeof(PQ), nameof(PQ.UpdateSubdivision))]
internal static class PQ_UpdateSubdivision_Patch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        // Remove the first call to UpdateTargetRelativity at the start of the method
        // Pattern: ldarg.0, call UpdateTargetRelativity
        var updateTargetRelativityMethod = SymbolExtensions.GetMethodInfo(() =>
            default(PQ).UpdateTargetRelativity()
        );

        matcher
            .MatchStartForward(
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Call, updateTargetRelativityMethod)
            )
            .ThrowIfInvalid("Could not find UpdateTargetRelativity call")
            .RemoveInstructions(2);

        return matcher.InstructionEnumeration();
    }
}

[HarmonyPatch(typeof(PQ), nameof(PQ.Subdivide))]
internal static class PQ_Subdivide_Patch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        // Replace all calls to UpdateVisibility with pop (to discard the instance on the stack)
        var updateVisibilityMethod = SymbolExtensions.GetMethodInfo<PQ>(pq =>
            pq.UpdateVisibility()
        );

        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Callvirt, updateVisibilityMethod))
            .Repeat(m => m.SetOpcodeAndAdvance(OpCodes.Pop));

        // Insert OnQuadSubdivided before the first QueueForNormalUpdate call
        var queueForNormalUpdateMethod = SymbolExtensions.GetMethodInfo<PQ>(pq =>
            pq.QueueForNormalUpdate()
        );

        matcher
            .Start()
            .MatchStartForward(new CodeMatch(OpCodes.Callvirt, queueForNormalUpdateMethod))
            .Insert(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(
                    OpCodes.Call,
                    SymbolExtensions.GetMethodInfo(() => OnQuadSubdivided(default))
                )
            );

        return matcher.InstructionEnumeration();
    }

    static void OnQuadSubdivided(PQ quad)
    {
        var batchPQS = quad.sphereRoot.GetComponent<BatchPQS>();
        if (batchPQS != null)
            batchPQS.OnQuadSubdivided(quad);
    }
}
