using System;
using System.Runtime.InteropServices;

namespace BurstPQS.Collections;

[StructLayout(LayoutKind.Sequential)]
public struct FixedArray30<T>
    where T : unmanaged
{
    T v0;
    T v1;
    T v2;
    T v3;
    T v4;
    T v5;
    T v6;
    T v7;
    T v8;
    T v9;
    T v10;
    T v11;
    T v12;
    T v13;
    T v14;
    T v15;
    T v16;
    T v17;
    T v18;
    T v19;
    T v20;
    T v21;
    T v22;
    T v23;
    T v24;
    T v25;
    T v26;
    T v27;
    T v28;
    T v29;

    public FixedArray30() { }

    public FixedArray30(T[] array)
    {
        CopyFrom(array);
    }

    public readonly int Length => 30;

    public unsafe T this[int index]
    {
        readonly get
        {
            if ((uint)index >= Length)
                BurstException.ThrowIndexOutOfRange();

            fixed (FixedArray30<T>* array = &this)
                return ((T*)array)[index];
        }
        set
        {
            if ((uint)index >= Length)
                BurstException.ThrowIndexOutOfRange();

            fixed (FixedArray30<T>* array = &this)
                ((T*)array)[index] = value;
        }
    }

    public unsafe void CopyFrom(T[] values)
    {
        if (values.Length != Length)
            throw new ArgumentException("values array must be exactly 30 elements long");

        fixed (T* src = values)
        fixed (FixedArray30<T>* array = &this)
        {
            T* dst = (T*)array;

            for (int i = 0; i < Length; ++i)
                dst[i] = src[i];
        }
    }
}
