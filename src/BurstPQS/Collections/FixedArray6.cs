using System.Runtime.InteropServices;
using Unity.Burst.CompilerServices;

namespace BurstPQS.Collections;

[StructLayout(LayoutKind.Sequential)]
public struct FixedArray6<T>
    where T : unmanaged
{
    T v0;
    T v1;
    T v2;
    T v3;
    T v4;
    T v5;

    public readonly int Length => 6;

    public unsafe T this[int index]
    {
        readonly get
        {
            if (Hint.Unlikely((uint)index >= Length))
                BurstException.ThrowIndexOutOfRange();

            fixed (FixedArray6<T>* array = &this)
                return ((T*)array)[index];
        }
        set
        {
            if (Hint.Unlikely((uint)index >= Length))
                BurstException.ThrowIndexOutOfRange();

            fixed (FixedArray6<T>* array = &this)
                ((T*)array)[index] = value;
        }
    }
}
