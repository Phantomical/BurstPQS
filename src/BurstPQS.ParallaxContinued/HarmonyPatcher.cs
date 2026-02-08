using HarmonyLib;
using UnityEngine;

namespace BurstPQS.ParallaxContinued;

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class HarmonyPatcher : MonoBehaviour
{
    static readonly Harmony Harmony = new("BurstPQS.ParallaxContinued");

    public static void ModuleManagerPostLoad()
    {
        var original = SymbolExtensions.GetMethodInfo<PQ>(pq => pq.SetVisible());

        // We handle manually calling Parallax methods at the right times, so
        // disable their patch
        Harmony.Unpatch(original, HarmonyPatchType.Prefix, "Parallax");
    }

    void Awake()
    {
        Harmony.PatchAll();
    }
}
