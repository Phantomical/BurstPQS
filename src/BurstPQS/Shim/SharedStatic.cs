using System;
using System.Runtime.CompilerServices;
using KSP.UI;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.LowLevel;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BackgroundResourceProcessing.Shim;

internal readonly unsafe struct SharedStatic<T>
    where T : unmanaged
{
    private readonly void* _buffer;

    public ref T Data => ref Unsafe.AsRef<T>(_buffer);

    public void* UnsafeDataPointer => _buffer;

    private SharedStatic(void* buffer)
    {
        _buffer = buffer;
    }

    public static SharedStatic<T> GetOrCreate<TContext>(uint alignment = 0) =>
        GetOrCreate(BurstRuntime.GetHashCode64<TContext>(), 0, alignment);

    public static SharedStatic<T> GetOrCreate<TContext, TSubContext>(uint alignment = 0) =>
        GetOrCreate(
            BurstRuntime.GetHashCode64<TContext>(),
            BurstRuntime.GetHashCode64<TSubContext>(),
            alignment
        );

    private static SharedStatic<T> GetOrCreate(long hash, long subhash, uint alignment) =>
        new(
            GetOrCreateInternal(
                hash,
                subhash,
                (uint)Unsafe.SizeOf<T>(),
                (uint)Math.Max(alignment == 0 ? 4 : alignment, UnsafeUtility.AlignOf<T>())
            )
        );

    [IgnoreWarning(1370)]
    private static unsafe void* GetOrCreateInternal(long hash, long subhash, uint size, uint align)
    {
        if (size == 0)
            throw new ArgumentException("size must be > 0");

        var hash128 = new Hash128((ulong)hash, (ulong)subhash);
        var result = BurstCompilerService.GetOrCreateSharedMemory(ref hash128, size, align);
        if (result is null)
            throw new InvalidOperationException("Unable to create a SharedStatic for this key");
        return result;
    }
}
