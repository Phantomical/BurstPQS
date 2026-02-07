using System;
using System.Collections.Generic;

namespace BurstPQS;

public class BatchPQSJobSet : IDisposable
{
    private const int MaxPoolItems = 128;
    private static readonly Stack<BatchPQSJobSet> Pool = [];

    internal static BatchPQSJobSet Acquire()
    {
        if (Pool.TryPop(out var jobSet))
            return jobSet;
        return new();
    }

    readonly List<JobData> jobs = [];

    internal BatchPQSJobSet() { }

    public void Add<T>(in T job)
        where T : struct
    {
        jobs.Add(JobData<T>.Create(job));
    }

    internal void BuildHeights(in BuildHeightsData data)
    {
        foreach (var job in jobs)
            job.BuildHeights(in data);
    }

    internal void BuildVertices(in BuildVerticesData data)
    {
        foreach (var job in jobs)
            job.BuildVertices(data);
    }

    internal void BuildMesh(in BuildMeshData data)
    {
        foreach (var job in jobs)
            job.BuildMesh(data);
    }

    internal void OnMeshBuilt(PQ quad)
    {
        foreach (var job in jobs)
            job.OnMeshBuilt(quad);
    }

    public void Dispose()
    {
        try
        {
            List<Exception> exceptions = null;

            foreach (var job in jobs)
            {
                try
                {
                    job.Dispose();
                }
                catch (Exception e)
                {
                    exceptions ??= [];
                    exceptions.Add(e);
                }
            }

            if (exceptions is null)
                return;
            else if (exceptions.Count == 1)
                throw exceptions[0];
            else
                throw new AggregateException(exceptions);
        }
        finally
        {
            if (Pool.Count < MaxPoolItems)
            {
                jobs.Clear();
                Pool.Push(this);
            }
        }
    }
}
