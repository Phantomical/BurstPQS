using BurstPQS.Mod;
using HarmonyLib;

namespace BurstPQS.Patches;

[HarmonyPatch(
    typeof(PQSMod_UVPlanetRelativePosition),
    nameof(PQSMod_UVPlanetRelativePosition.OnQuadUpdateNormals)
)]
internal static class PQSMod_UVPlanetRelativePosition_OnQuadUpdateNormals_Patch
{
    static bool Prefix(PQ quad)
    {
        UVPlanetRelativePosition.UpdateQuadNormals(quad);
        return false;
    }
}
