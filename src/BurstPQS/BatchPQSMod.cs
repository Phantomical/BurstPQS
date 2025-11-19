using System;
using Unity.Jobs;

namespace BurstPQS;

/// <summary>
/// Interface for the stateful part of a BatchPQSMod.
/// </summary>
///
/// <remarks>
/// These will be created at the start of the quad build process, after
/// <c>OnQuadPreBuild</c> is called. There may be multiple PQS quads being built
/// at the same time, so you should return a new object that will store the
/// relevant state.
///
/// If your PQSMod is stateless then you can can just directly implement this
/// interface and avoid needing to create a new object. You can also key off the
/// <see cref="QuadBuildData"/> instance, though this is not the recommended
/// way to do this.
/// </remarks>
public interface IBatchPQSModState
{
    /// <summary>
    /// Schedule a job to be completed as part of building vertex heights.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="handle"></param>
    /// <returns>A new job handle that will be used to continue the chain.</returns>
    ///
    /// <remarks>
    /// The returned job handle should include all jobs created by this step
    /// that refer to any data stored in <paramref name="data"/>.
    /// </remarks>
    JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle);

    /// <summary>
    /// Schedule a job to be run as part of building the vertices themselves.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="handle"></param>
    /// <returns>A new job handle that will be used to continue the chain.</returns>
    ///
    /// <remarks>
    /// The returned job handle should include all jobs created by this step
    /// that refer to any data stored in <paramref name="data"/>.
    /// </remarks>
    JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle);

    /// <summary>
    /// Called on the main thread when the all the vertices have been built for
    /// this patch. Use this method to perform any writeback to the actual PQSMod
    /// and to dispose of any native resources that weren't disposed of after the
    /// job completed.
    /// </summary>
    void OnBuildComplete(QuadBuildData data);
}

public abstract class BatchPQSMod : IDisposable
{
    /// <summary>
    /// Called during PQS setup, after the setup for all <see cref="PQSMod"/>s
    /// have been called.
    /// </summary>
    ///
    /// <remarks>
    /// Use this to set up native data structures. Dispose will not be called
    /// if an error happens while <see cref="BatchPQS"/> is constructing the
    /// <see cref="BatchPQSMod"/>s.
    /// </remarks>
    public virtual void OnSetup() { }

    /// <summary>
    /// Called during PQS teardown. All scheduled jobs will be completed at
    /// this point as long as they were included in the dependencies of the
    /// <see cref="JobHandle"/>s returned from <see cref="IBatchPQSModState"/>.
    /// </summary>
    public virtual void Dispose() { }

    /// <summary>
    /// Get a <see cref="IBatchPQSModState"/> that will be used to build a quad.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public abstract IBatchPQSModState GetState(QuadBuildData data);
}

public abstract class BatchPQSMod<T>(T mod) : BatchPQSMod
    where T : PQSMod
{
    protected T mod = mod;

    public T Mod => mod;

    public override string ToString()
    {
        if (mod is null)
            return $"null ({GetType().Name})";
        return $"{mod.name} ({GetType().Name})";
    }
}
