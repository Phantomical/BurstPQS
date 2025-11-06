using System.Runtime.CompilerServices;
using UnityEngine;

namespace Unity.Burst.LowLevel;

internal static class BurstCompilerService
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern unsafe void* GetOrCreateSharedMemory(
        ref Hash128 key,
        uint size_of,
        uint alignment
    );
}
