using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BurstPQS.Util;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

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
        get
        {
            Hint.Assume(length >= 0);
            return length;
        }
    }

    public ref T this[int index]
    {
        get
        {
            // Hint.Assume(index >= 0 && index < Length);
            if (Hint.Unlikely(index < 0 || index > Length))
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
        // Hint.Assume(length >= 0);
        // Hint.Assume(length == 0 || data is not null);
        if (length < 0)
            BurstException.ThrowArgumentOutOfRange();

        // This bit makes codegen quite a bit worse
        if (!BurstUtil.IsBurstCompiled)
        {
            if (data is null && length > 0)
                BurstException.ThrowArgumentOutOfRange();
        }

        this.data = data;
        this.length = length;
    }

    public MemorySpan(NativeArray<T> array)
        : this((T*)array.GetUnsafePtr(), array.Length) { }

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

    public readonly NativeArray<T> AsNativeArray()
    {
        return NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
            data,
            Length,
            Allocator.Invalid
        );
    }

    #region IEnumerator
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct Enumerator(MemorySpan<T> span) : IEnumerator<T>
    {
        T* current = span.data - 1;
        readonly T* end = span.data + span.Length;

        public readonly ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref *current;
        }
        readonly T IEnumerator<T>.Current => Current;
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

public static class MemorySpanExt
{
    public static unsafe double4 GetVec4(this MemorySpan<double> span, int index)
    {
        if (Hint.Unlikely(index < 0 || index + 4 > span.Length))
            BurstException.ThrowIndexOutOfRange();

        return *(double4*)&span.GetDataPtr()[index];
    }

    public static unsafe void SetVec4(this MemorySpan<double> span, int index, double4 v)
    {
        if (Hint.Unlikely(index < 0 || index + 4 > span.Length))
            BurstException.ThrowIndexOutOfRange();

        *(double4*)&span.GetDataPtr()[index] = v;
    }
}
