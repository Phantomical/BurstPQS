using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace BurstPQS.Collections;

internal unsafe struct OwnedBuffer : IDisposable
{
    public void* Data { get; private set; }
    public int Length { get; private set; }
    public Allocator Allocator { get; private set; }

    public OwnedBuffer(int length, Allocator allocator = Allocator.Temp)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        var data = UnsafeUtility.Malloc(length, 16, allocator);
        if (data is null)
            throw new Exception("malloc returned null");

        Data = data;
        Length = length;
        Allocator = allocator;
    }

    public readonly void Clear()
    {
        if (Data is not null)
            UnsafeUtility.MemClear(Data, Length);
    }

    public void Dispose()
    {
        if (Data is not null)
            UnsafeUtility.Free(Data, Allocator);
        Data = null;
    }
}
