using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst.CompilerServices;

namespace BurstPQS.Collections;

public readonly struct ReadOnlyMemorySpan<T>(MemorySpan<T> span) : IEnumerable<T>
    where T : unmanaged
{
    readonly MemorySpan<T> span = span;

    public int Length => span.Length;

    public T this[int index] => span[index];

    public ReadOnlyMemorySpan()
        : this(default) { }

    public ReadOnlyMemorySpan<T> Slice(int start) => new(span.Slice(start));

    public ReadOnlyMemorySpan<T> Slice(int start, int length) => new(span.Slice(start, length));

    #region IEnumerator
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(ReadOnlyMemorySpan<T> span) : IEnumerator<T>
    {
        MemorySpan<T>.Enumerator enumerator = span.span.GetEnumerator();

        public readonly T Current => enumerator.Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => enumerator.MoveNext();

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => enumerator.Dispose();
    }
    #endregion
}
