using System;
using System.Collections.Generic;

namespace BurstPQS;

public class BatchPQSJobSet : IDisposable
{
    readonly List<JobData> jobs = [];

    public void Add<T>(in T job)
        where T : struct
    {
        jobs.Add(new JobData<T>(job));
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
        if (exceptions.Count == 1)
            throw exceptions[0];

        throw new AggregateException(exceptions);
    }
}
