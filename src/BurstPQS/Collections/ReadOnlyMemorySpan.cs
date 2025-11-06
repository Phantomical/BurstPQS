using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst.CompilerServices;
using Unity.Collections;

namespace BurstPQS.Collections;

public readonly unsafe struct ReadOnlyMemorySpan<T>(MemorySpan<T> span) : IEnumerable<T>
    where T : unmanaged
{
    [ReadOnly]
    readonly T* data = span.GetDataPtr();
    readonly int length = span.Length;

    public readonly int Length
    {
        [return: AssumeRange(0, int.MaxValue)]
        get
        {
            Hint.Assume(length >= 0);
            return length;
        }
    }

    public readonly ref T this[int index]
    {
        get
        {
            // Hint.Assume(index >= 0 && index < Length);
            if (Hint.Unlikely(index < 0 || index > Length))
                BurstException.ThrowIndexOutOfRange();

            return ref data[index];
        }
    }

    public ReadOnlyMemorySpan()
        : this(default) { }

    public ReadOnlyMemorySpan(T* data, int length)
        : this(new MemorySpan<T>(data, length)) { }

    public ReadOnlyMemorySpan<T> Slice(int start) => new(span.Slice(start));

    public ReadOnlyMemorySpan<T> Slice(int start, int length) => new(span.Slice(start, length));

    public static implicit operator ReadOnlyMemorySpan<T>(MemorySpan<T> span) => new(span);

    #region IEnumerator
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(ReadOnlyMemorySpan<T> span) : IEnumerator<T>
    {
        MemorySpan<T>.Enumerator enumerator = new MemorySpan<T>(
            span.data,
            span.Length
        ).GetEnumerator();

        public readonly T Current => enumerator.Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => enumerator.MoveNext();

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => enumerator.Dispose();
    }
    #endregion
}
