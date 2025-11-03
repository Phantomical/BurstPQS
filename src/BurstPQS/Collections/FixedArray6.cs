using System.Runtime.InteropServices;

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
            if ((uint)index >= Length)
                BurstException.ThrowIndexOutOfRange();

            fixed (FixedArray6<T>* array = &this)
                return ((T*)array)[index];
        }
        set
        {
            if ((uint)index >= Length)
                BurstException.ThrowIndexOutOfRange();

            fixed (FixedArray6<T>* array = &this)
                ((T*)array)[index] = value;
        }
    }
}
