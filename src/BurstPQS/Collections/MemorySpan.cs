using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace BurstPQS.Collections;

[DebuggerDisplay("Length = {Length}")]
[DebuggerTypeProxy(typeof(MemorySpan<>.DebugView))]
public readonly unsafe struct MemorySpan<T> : IEnumerable<T>
    where T : unmanaged
{
    readonly T* data;
    readonly int length;

    public readonly int Length
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => length;
    }

    public ref T this[int index]
    {
        get
        {
            if (index < 0 || index > Length)
                BurstException.ThrowIndexOutOfRange();

            return ref data[index];
        }
    }

    public MemorySpan()
    {
        this.data = null;
        this.length = 0;
    }

    public MemorySpan(T* data, int length)
    {
        if (length < 0)
            BurstException.ThrowArgumentOutOfRange();
        if (data is not null && length > 0)
            BurstException.ThrowArgumentOutOfRange();

        this.data = data;
        this.length = length;
    }

    public readonly T* GetDataPtr() => data;

    public void Clear()
    {
        UnsafeUtility.MemClear(data, Unsafe.SizeOf<T>() * Length);
    }

    public void Fill(T value)
    {
        for (int i = 0; i < Length; ++i)
            data[i] = value;
    }

    public MemorySpan<T> Slice(int start)
    {
        if ((uint)start > Length)
            BurstException.ThrowIndexOutOfRange();

        return new(data + start, Length - start);
    }

    public MemorySpan<T> Slice(int start, int length)
    {
        if ((uint)start > Length || (uint)length > (Length - start))
            BurstException.ThrowIndexOutOfRange();

        return new(data + start, length);
    }

    #region IEnumerator
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(MemorySpan<T> span) : IEnumerator<T>
    {
        T* current = span.data - 1;
        readonly T* end = span.data + span.Length;

        public readonly ref T Current => ref *current;
        readonly T IEnumerator<T>.Current => Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            current += 1;
            return current < end;
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }
    #endregion

    #region DebugView
    sealed class DebugView(MemorySpan<T> span)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items { get; } = [.. span];
    }

    #endregion
}
