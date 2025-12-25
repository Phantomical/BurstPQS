using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BurstPQS.Async;

namespace BurstPQS.Collections;

[StructLayout(LayoutKind.Sequential)]
public struct FixedArray4<T> : IEnumerable<T>
{
    T v0;
    T v1;
    T v2;
    T v3;

    public readonly int Length => 4;

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get
        {
            if ((uint)index >= Length)
                BurstException.ThrowIndexOutOfRange();

            ref T slot = ref Unsafe.Add(
                ref Unsafe.As<FixedArray4<T>, T>(ref Unsafe.AsRef(in this)),
                index
            );
            return slot;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if ((uint)index >= Length)
                BurstException.ThrowIndexOutOfRange();

            ref T slot = ref Unsafe.Add(ref Unsafe.As<FixedArray4<T>, T>(ref this), index);
            slot = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() => new(this);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    public struct Enumerator(FixedArray4<T> array) : IEnumerator<T>
    {
        FixedArray4<T> array = array;
        int index = -1;

        public readonly T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => array[index];
        }
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            index += 1;
            return index < array.Length;
        }

        public void Reset()
        {
            index = -1;
        }

        public void Dispose() { }
    }
}
