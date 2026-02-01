using System;
using System.Runtime.InteropServices;
using Unity.Jobs;

namespace BurstPQS.Util;

/// <summary>
/// A helper struct that allows you to pass managed objects to jobs.
/// </summary>
/// <typeparam name="T"></typeparam>
internal struct ObjectHandle<T>(T value) : IDisposable
    where T : class
{
    GCHandle handle = GCHandle.Alloc(value);

    public T Target => (T)handle.Target;

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
