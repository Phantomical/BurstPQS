using System.Collections.Generic;

namespace BurstPQS.Util;

internal static class ListExt
{
    public static T[] GetBackingArray<T>(this List<T> list) => list._items;
}
