using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;

namespace BurstPQS.Async;

internal class JobSynchronizationContext : SynchronizationContext
{
    private readonly struct WorkRequest(SendOrPostCallback callback, object state)
    {
        private readonly SendOrPostCallback callback = callback;
        private readonly object state = state;

        public void Invoke()
        {
            callback(state);
        }
    }

    private readonly struct JobWaitRequest(JobHandle handle, TaskCompletionSource<object> source)
    {
        public readonly JobHandle handle = handle;
        public readonly TaskCompletionSource<object> source = source;

        public void Complete()
        {
            try
            {
                handle.Complete();
                source.SetResult(null);
            }
            catch (Exception e)
            {
                source.SetException(e);
            }
        }
    }

    private const int JobScheduleInterval = 128;

    private readonly Queue<WorkRequest> WorkQueue = [];
    private readonly Queue<JobWaitRequest> JobQueue = [];
    private int JobScheduleCounter = JobScheduleInterval;

    public override void Post(SendOrPostCallback cb, object state)
    {
        WorkQueue.Enqueue(new(cb, state));
    }

    public override void Send(SendOrPostCallback cb, object state)
    {
        cb(state);
    }

    void WaitJob(JobHandle handle, TaskCompletionSource<object> tcs)
    {
        JobQueue.Enqueue(new JobWaitRequest(handle, tcs));

        if (JobScheduleCounter-- <= 0)
        {
            JobHandle.ScheduleBatchedJobs();
            JobScheduleCounter = JobScheduleInterval;
        }
    }

    void DrainTasks()
    {
        List<Exception> exceptions = null;

        JobHandle.ScheduleBatchedJobs();

        while (true)
        {
            while (WorkQueue.Count != 0)
            {
                var item = WorkQueue.Dequeue();

                try
                {
                    item.Invoke();
                }
                catch (Exception e)
                {
                    exceptions ??= [];
                    exceptions.Add(e);
                }
            }

            if (JobQueue.Count == 0)
                break;

            var jobitem = JobQueue.Dequeue();
            jobitem.Complete();

            while (JobQueue.Count != 0)
            {
                jobitem = JobQueue.Peek();
                if (!jobitem.handle.IsCompleted)
                    break;

                JobQueue.Dequeue();
                jobitem.Complete();
            }
        }

        if (exceptions is not null)
        {
            if (exceptions.Count == 1)
                throw exceptions[0];

            throw new AggregateException(exceptions);
        }
    }

    public static async Task WaitForJob(JobHandle handle)
    {
        if (Current is not JobSynchronizationContext context)
            throw new InvalidOperationException(
                "Cannot wait for a JobHandle outside of a JobSynchronizationContext"
            );

        var tcs = new TaskCompletionSource<object>();
        var task = tcs.Task;

        context.WaitJob(handle, tcs);

        await task;
    }

    public static ContextGuard Enter() => new();

    public readonly struct ContextGuard : IDisposable
    {
        readonly SynchronizationContext prev;
        readonly JobSynchronizationContext ctx;

        public ContextGuard()
        {
            ctx = new JobSynchronizationContext();
            prev = Current;
            SetSynchronizationContext(ctx);
        }

        public void Dispose()
        {
            try
            {
                ctx.DrainTasks();
            }
            finally
            {
                SetSynchronizationContext(prev);
            }
        }
    }
}
