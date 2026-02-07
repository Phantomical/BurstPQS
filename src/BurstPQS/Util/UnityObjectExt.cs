using System;
using System.Runtime.CompilerServices;

namespace BurstPQS.Util;

internal static class UnityObjectExt
{
    /// <summary>
    /// True if this <paramref name="unityObject"/> reference is <c>null</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNullRef(this UnityEngine.Object unityObject)
    {
        return ReferenceEquals(unityObject, null);
    }

    /// <summary>
    /// True if this <paramref name="unityObject"/> reference is not <c>null</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNullRef(this UnityEngine.Object unityObject)
    {
        return !ReferenceEquals(unityObject, null);
    }

    /// <summary>
    /// True if this <paramref name="unityObject"/> reference is <c>null</c> or if the instance is destroyed<br/>
    /// Equivalent as testing <c><paramref name="unityObject"/> == null</c> but 4-5 times faster.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNullOrDestroyed(this UnityEngine.Object unityObject)
    {
        return ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero;
    }

    /// <summary>
    /// True if this <paramref name="unityObject"/> reference is not <c>null</c> and the instance is not destroyed<br/>
    /// Equivalent as testing <c><paramref name="unityObject"/> != null</c> but 4-5 times faster.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNullOrDestroyed(this UnityEngine.Object unityObject)
    {
        return !ReferenceEquals(unityObject, null) && unityObject.m_CachedPtr != IntPtr.Zero;
    }
}
