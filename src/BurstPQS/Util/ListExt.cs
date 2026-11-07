using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BurstPQS.Util;

internal static class ListExt
{
    // Must match the fields of List<T> in KSP's mscorlib.
    class ListFields<T>
    {
        public T[] _items;
        public int _size;
        public int _version;
        public object _syncRoot;
    }

    public static T[] GetBackingArray<T>(this List<T> list) =>
        Unsafe.As<ListFields<T>>(list)._items;
}
