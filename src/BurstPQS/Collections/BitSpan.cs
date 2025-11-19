using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BurstPQS.Util;
using Unity.Burst.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using static Unity.Burst.Intrinsics.X86.Bmi2;

namespace BurstPQS.Collections;

[DebuggerDisplay("Count = {Count}/{Capacity}")]
[DebuggerTypeProxy(typeof(DebugView))]
internal struct BitSpan(MemorySpan<ulong> bits) : IEnumerable<int>
{
    const int ULongBits = 64;

    MemorySpan<ulong> bits = bits;

    public readonly int Capacity
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits.Length * ULongBits;
    }
    public readonly int Words
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits.Length;
    }
    public readonly int Count
    {
        get
        {
            int count = 0;
            foreach (ulong word in bits)
                count += MathUtil.PopCount(word);
            return count;
        }
    }

    public readonly MemorySpan<ulong> Span => bits;

    public readonly bool this[int key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (key < 0 || key >= Capacity)
                BurstException.ThrowIndexOutOfRange();

            var word = key / ULongBits;
            var bit = key % ULongBits;

            return (bits[word] & (1ul << bit)) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (key < 0 || key >= Capacity)
                BurstException.ThrowIndexOutOfRange();

            var word = key / ULongBits;
            var bit = key % ULongBits;
            var mask = 1ul << bit;

            if (value)
                bits[word] |= mask;
            else
                bits[word] &= ~mask;
        }
    }

    public readonly bool this[uint key]
    {
        get => this[(int)key];
        set => this[(int)key] = value;
    }

    public unsafe BitSpan(ulong* bits, int length)
        : this(new MemorySpan<ulong>(bits, length)) { }

    public readonly bool Contains(int key)
    {
        if (key < 0 || key >= Capacity)
            return false;
        return this[key];
    }

    public readonly int GetCount()
    {
        int count = 0;
        foreach (ulong word in bits)
            count += MathUtil.PopCount(word);
        return count;
    }

    public bool Add(int key) => this[key] = true;

    public bool Remove(int key) => this[key] = false;

    public readonly void Clear() => bits.Clear();

    public readonly void Fill(bool value) => bits.Fill(value ? ulong.MaxValue : 0);

    public void AndWith(BitSpan other)
    {
        if (bits.Length < other.bits.Length)
            BurstException.ThrowArgumentOutOfRange();

        for (int i = 0; i < other.bits.Length; ++i)
            bits[i] &= other.bits[i];
    }

    public void Assign(ulong[] other)
    {
        var length = Math.Min(bits.Length, other.Length);

        for (int i = 0; i < length; ++i)
            bits[i] = other[i];
    }

    public void Assign(BitSpan other)
    {
        var length = Math.Min(bits.Length, other.bits.Length);

        for (int i = 0; i < length; ++i)
            bits[i] = other.bits[i];
    }

    public void ClearUpFrom(int index)
    {
        if (index < 0)
            BurstException.ThrowIndexOutOfRange();

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = MaskLo(bit);

        if (word >= bits.Length)
            return;

        bits[word] &= mask;

        for (int i = word + 1; i < bits.Length; ++i)
            bits[i] = 0;
    }

    public void ClearUpTo(int index)
    {
        if (index < 0)
            BurstException.ThrowIndexOutOfRange();

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = ~MaskLo(bit);

        if (word >= bits.Length)
            Clear();
        else
        {
            for (int i = 0; i < word; ++i)
                bits[i] = 0;

            bits[word] &= mask;
        }
    }

    [IgnoreWarning(1370)]
    public void ClearOutsideRange(int start, int end)
    {
        if (start < 0)
            BurstException.ThrowIndexOutOfRange();
        if (end < start)
            BurstException.ThrowIndexOutOfRange();
        if (end > Capacity)
            BurstException.ThrowIndexOutOfRange();

        int sword = start / ULongBits;
        int eword = end / ULongBits;
        int sbit = start % ULongBits;
        int ebit = end % ULongBits;

        for (int i = 0; i < sword; ++i)
            bits[i] = 0;

        if (sword >= bits.Length)
            return;

        ulong smask = ulong.MaxValue << sbit;
        ulong emask = MaskLo(ebit);

        bits[sword] &= smask;

        if (eword >= bits.Length)
            return;

        bits[eword] &= emask;

        for (int i = eword + 1; i < bits.Length; ++i)
            bits[i] = 0;
    }

    public void SetUpTo(int index)
    {
        if (index < 0 || index > Capacity)
            BurstException.ThrowIndexOutOfRange();

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = MaskLo(bit);

        for (int i = 0; i < word; ++i)
            bits[i] = ulong.MaxValue;

        if (word < bits.Length)
            bits[word] |= mask;
    }

    [IgnoreWarning(1370)]
    public void CopyFrom(BitSpan other)
    {
        if (other.bits.Length != bits.Length)
            BurstException.ThrowArgumentOutOfRange();

        for (int i = 0; i < bits.Length; ++i)
            bits[i] = other.bits[i];
    }

    [IgnoreWarning(1370)]
    public void CopyInverseFrom(BitSpan other)
    {
        if (other.bits.Length != bits.Length)
            BurstException.ThrowArgumentOutOfRange();

        for (int i = 0; i < bits.Length; ++i)
            bits[i] = ~other.bits[i];
    }

    [IgnoreWarning(1370)]
    public void RemoveAll(BitSpan other)
    {
        if (other.bits.Length != bits.Length)
            BurstException.ThrowArgumentOutOfRange();

        for (int i = 0; i < bits.Length; ++i)
            bits[i] &= ~other.bits[i];
    }

    static ulong MaskLo(int bit)
    {
        if (IsBmi2Supported)
            return bzhi_u64(ulong.MaxValue, (ulong)bit);
        else if (bit >= 64)
            return ulong.MaxValue;
        else
            return (1ul << bit) - 1;
    }

    #region operators
    public unsafe static bool operator ==(BitSpan a, BitSpan b)
    {
        if (a.Words != b.Words)
            return false;

        return UnsafeUtility.MemCmp(a.bits.GetDataPtr(), b.bits.GetDataPtr(), a.Words) == 0;
    }

    public static bool operator !=(BitSpan a, BitSpan b)
    {
        return !(a == b);
    }
    #endregion

    public override readonly bool Equals(object obj) => false;

    // This suppresses the warning but will always throw an exception.
    public override readonly int GetHashCode() => bits.GetHashCode();

    #region IEnumerator<T>
    public readonly Enumerator GetEnumerator() => new(this);

    public readonly Enumerator GetEnumeratorAt(int index) => new(this, index);

    readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(BitSpan set) : IEnumerator<int>, IEnumerable<int>
    {
        readonly MemorySpan<ulong> words = set.bits;
        int index = -1;
        int bit = -1;
        ulong word = 0;

        public readonly int Current => index * ULongBits + bit;
        readonly object IEnumerator.Current => Current;

        public Enumerator(BitSpan set, int offset)
            : this(set)
        {
            index = offset / ULongBits;
            bit = offset % ULongBits;

            if (index < words.Length)
                word = words[index] & ~((1ul << bit) - 1);
        }

        public bool MoveNext()
        {
            while (true)
            {
                if (word != 0)
                {
                    bit = MathUtil.TrailingZeroCount(word);
                    word ^= 1ul << bit;
                    return true;
                }

                index += 1;
                if (index >= words.Length)
                    return false;

                word = words[index];
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public readonly void Dispose() { }

        public readonly Enumerator GetEnumerator() => this;

        readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    #endregion

    internal sealed class DebugView(BitSpan span)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items { get; } = [.. span];
    }
}
