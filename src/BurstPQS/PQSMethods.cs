using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

#pragma warning disable CS8321 // Local function is declared but never used

namespace BurstPQS;

[HarmonyPatch]
[HarmonyPriority(Priority.Last)]
internal static class PQSMethods
{
    const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static void Empty() { }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(PQSMethods), nameof(Empty))]
    public static void Start(PQS pqs)
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _) =>
            CallMethod(typeof(PQS).GetMethod(nameof(Start), Instance));
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(PQSMethods), nameof(Empty))]
    public static void OnDestroy(PQS pqs)
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _) =>
            CallMethod(typeof(PQS).GetMethod(nameof(OnDestroy), Instance));
    }

    private static List<CodeInstruction> CallMethod(MethodBase method)
    {
        int pcount = method.GetParameters().Length;
        if (!method.IsStatic)
            pcount += 1;

        var insts = new List<CodeInstruction>(pcount + 2);
        for (int i = 0; i < pcount; ++i)
        {
            insts.Add(
                i switch
                {
                    0 => new CodeInstruction(OpCodes.Ldarg_0),
                    1 => new CodeInstruction(OpCodes.Ldarg_1),
                    2 => new CodeInstruction(OpCodes.Ldarg_2),
                    3 => new CodeInstruction(OpCodes.Ldarg_3),
                    _ => i <= 255
                        ? new CodeInstruction(OpCodes.Ldarg_S, (byte)i)
                        : new CodeInstruction(OpCodes.Ldarg, i),
                }
            );
        }

        if (method.IsVirtual)
            insts.Add(new CodeInstruction(OpCodes.Callvirt, method));
        else
            insts.Add(new CodeInstruction(OpCodes.Call, method));

        insts.Add(new CodeInstruction(OpCodes.Ret));

        return insts;
    }
}
