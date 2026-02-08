using HarmonyLib;
using Parallax;

namespace BurstPQS.ParallaxContinued.Patches;

[HarmonyPatch(typeof(PQ), nameof(PQ.SetVisible))]
internal static class PQ_SetVisible_Patch
{
    static void Prefix(PQ __instance)
    {
        if (__instance.isVisible)
            return;
        if (!__instance.isBuilt)
            return;

        // Quad is already built but now visible - For example going back down a subdivision level.
        //
        // The other case is handled by the parallax BurstPQS mod.
        ScatterComponent.OnQuadVisible(__instance);
    }
}
