using HarmonyLib;
using Parallax;

namespace BurstPQS.ParallaxContinued.Patches;

[HarmonyPatch(typeof(PQSMod_Parallax), nameof(PQSMod_Parallax.OnQuadBuilt))]
internal static class PQSMod_Parallax_OnQuadBuilt_Patch
{
    static void Postfix(PQ quad)
    {
        if (!quad.isVisible)
            return;

        ScatterComponent.OnQuadVisibleBuilt(quad);
    }
}
