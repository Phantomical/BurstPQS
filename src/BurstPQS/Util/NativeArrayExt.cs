using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace BurstPQS.Util;

public static unsafe class NativeArrayExt
{
    /// <summary>
    /// Zero out all the elements of the native array.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="array"></param>
    public static void Clear<T>(this NativeArray<T> array)
        where T : unmanaged
    {
        UnsafeUtility.MemClear(array.GetUnsafePtr(), array.Length * sizeof(T));
    }
}
