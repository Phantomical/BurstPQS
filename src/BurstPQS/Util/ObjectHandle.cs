using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Jobs;

namespace BurstPQS.Util;

/// <summary>
/// A helper struct that allows you to pass managed objects to jobs.
/// </summary>
/// <typeparam name="T"></typeparam>
public struct ObjectHandle<T>(T value, GCHandleType type) : IDisposable
    where T : class
{
    GCHandle handle = GCHandle.Alloc(value, type);

    public T Target
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (T)handle.Target;
    }

    public bool IsAllocated => handle.IsAllocated;

    public ObjectHandle(T value)
        : this(value, GCHandleType.Normal) { }

    public void Dispose() => handle.Free();

    public void Dispose(JobHandle job)
    {
        new DisposeJob { handle = this }.Schedule(job);
    }

    struct DisposeJob : IJob
    {
        public ObjectHandle<T> handle;

        public void Execute() => handle.Dispose();
    }
}
