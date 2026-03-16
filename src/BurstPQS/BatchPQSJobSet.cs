using System;
using System.Collections.Generic;

namespace BurstPQS;

/// <summary>
/// A container for PQS jobs that will be in the build job.
/// </summary>
///
/// <remarks>
/// You get handed a reference to this in
/// <see cref="BatchPQSMod.OnQuadPreBuild(PQ, BatchPQSJobSet)"/>. Call
/// <see cref="Add{T}(in T)"/> to add a struct to be executed as part of the
/// build job.
/// </remarks>
public class BatchPQSJobSet : IDisposable
{
    private static readonly Stack<BatchPQSJobSet> Pool = [];

    internal static BatchPQSJobSet Acquire()
    {
        if (Pool.TryPop(out var jobSet))
            return jobSet;
        return new();
    }

    readonly List<JobData> jobs = [];

    internal BatchPQSJobSet() { }

    /// <summary>
    /// Add a job to be executed as part of the build job.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="job"></param>
    ///
    /// <remarks>
    /// The struct can implement any one of a number of traits. If it does, then
    /// the trait methods will be called at various phases in during the quad
    /// build. The available traits are:
    ///
    /// <list type="bullet">
    ///   <item><see cref="IBatchPQSHeightJob"/></item>
    ///   <item><see cref="IBatchPQSVertexJob"/></item>
    ///   <item><see cref="IBatchPQSMeshJob"/></item>
    ///   <item><see cref="IBatchPQSMeshBuiltJob"/></item>
    ///   <item><see cref="IDisposable"/></item>
    /// </list>
    ///
    /// See the BurstPQS wiki on github for a full description of what these
    /// interfaces mean and when you should implement them.
    /// </remarks>
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
            jobs.Clear();
            Pool.Push(this);
        }
    }
}
