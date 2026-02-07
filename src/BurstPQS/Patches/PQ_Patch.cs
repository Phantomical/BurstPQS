using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BurstPQS.Util;
using HarmonyLib;

namespace BurstPQS.Patches;

[HarmonyPatch]
[HarmonyDebug]
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
        var batchPQS = quad.sphereRoot.GetComponent<BatchPQS>();
        if (batchPQS.IsNullOrDestroyed())
            quad.Build();
        else
            batchPQS.BuildDeferred(quad);
    }
}
