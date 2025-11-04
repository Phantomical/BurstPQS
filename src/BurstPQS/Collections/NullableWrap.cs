using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BurstPQS.Collections;

/// <summary>
/// An equivalent type to <c>T?</c> that can be passed into and out of burst
/// methods. It can be implicitly converted to and from <c>T?</c>.
/// </summary>
public readonly struct NullableWrap<T>
    where T : unmanaged
{
    [MarshalAs(UnmanagedType.U1)]
    readonly bool hasValue;
    readonly T value;

    public bool HasValue => hasValue;
    public T Value => value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NullableWrap()
    {
        hasValue = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NullableWrap(T value)
    {
        hasValue = true;
        this.value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator NullableWrap<T>(T? val) => val is T inner ? new(inner) : new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator NullableWrap<T>(T val) => new(val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator T?(NullableWrap<T> wrap) => wrap.hasValue ? wrap.Value : null;
}
