namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSMod_QuadMeshColliders))]
public class QuadMeshColliders(PQSMod_QuadMeshColliders mod)
    : BatchPQSMod<PQSMod_QuadMeshColliders>(mod)
{
    // Replaces the stock OnQuadBuilt so the collider is cooked off the main
    // thread. OnQuadDestroy is still handled by the stock mod.
    public override void OnQuadBuilt(PQ quad)
    {
        if (quad.subdivision < mod.minLevel)
            return;

        QuadColliderBaker.Enqueue(quad, mod);
    }
}
