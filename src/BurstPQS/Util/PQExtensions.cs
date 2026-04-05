namespace BurstPQS.Util;

internal static class PQExtensions
{
    internal static bool IsSafeToSubdivide(this PQ q)
    {
        if (q.IsNullOrDestroyed())
            return false;
        if (!q.isActive || q.isSubdivided)
            return false;

        return q.north.IsNotNullOrDestroyed()
            && q.south.IsNotNullOrDestroyed()
            && q.east.IsNotNullOrDestroyed()
            && q.west.IsNotNullOrDestroyed();
    }

    internal static bool IsSafeToCollapse(this PQ q)
    {
        return q.IsNotNullOrDestroyed() && q.isActive && q.isSubdivided;
    }
}
