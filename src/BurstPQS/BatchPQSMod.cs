using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;

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
    void OnQuadBuilt(QuadBuildData data);
}

public abstract class BatchPQSModState : IDisposable, IBatchPQSModState
{
    public virtual JobHandle ScheduleBuildHeights(QuadBuildData data, JobHandle handle) => handle;

    public virtual JobHandle ScheduleBuildVertices(QuadBuildData data, JobHandle handle) => handle;

    public virtual void OnQuadBuilt(QuadBuildData data) { }

    public virtual void Dispose() { }
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
    /// Called just after starting to build the quad. Can return a
    /// <see cref="IBatchPQSModState"/> that will be used for future callbacks
    /// relating to this quad.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public virtual IBatchPQSModState OnQuadPreBuild(QuadBuildData data) => null;

    /// <summary>
    /// Called during PQS teardown. All scheduled jobs will be completed at
    /// this point as long as they were included in the dependencies of the
    /// <see cref="JobHandle"/>s returned from <see cref="IBatchPQSModState"/>.
    /// </summary>
    public virtual void Dispose() { }

    #region Registry
    static readonly Dictionary<Type, Type> ModTypes = [];

    /// <summary>
    /// Register a <see cref="BatchPQSModV1"/> adapter for a <see cref="PQSMod"/>
    /// type.
    /// </summary>
    /// <param name="batchMod">The type of the <see cref="BatchPQSModV1"/> adapter.</param>
    /// <param name="mod">The type of the <see cref="PQSMod"/>.</param>
    /// <exception cref="ArgumentException"></exception>
    public static void RegisterBatchPQSMod(Type batchMod, Type mod)
    {
        if (!typeof(PQSMod).IsAssignableFrom(mod))
            throw new ArgumentException("type does not inherit from PQSMod", nameof(mod));

        var batchPqsModType = typeof(BatchPQSModV1<>).MakeGenericType(mod);
        if (!batchPqsModType.IsAssignableFrom(batchMod))
            throw new ArgumentException(
                $"type does not inherit from {batchPqsModType.Name}",
                nameof(batchMod)
            );

        var ctor = batchMod.GetConstructor([mod]);
        if (ctor is null)
            throw new ArgumentException(
                $"{batchMod.Name} does not have a public single argument constructor taking a parameter of type {mod.Name}",
                nameof(batchMod)
            );

        if (ModTypes.TryGetValue(mod, out var prev))
        {
            Debug.LogWarning($"Multiple BatchPQSMods registered for PQSMod {mod.Name}:");
            Debug.LogWarning($"  - {prev.Name}");
            Debug.LogWarning($"  - {batchMod.Name}");
            return;
        }

        ModTypes.Add(mod, batchMod);
    }

    public static BatchPQSMod Create(PQSMod mod)
    {
        var type = mod.GetType();

        if (ModTypes.TryGetValue(type, out var batchMod))
            return (BatchPQSMod)Activator.CreateInstance(batchMod, [mod]);

        var onQuadPreBuild = type.GetMethod(nameof(PQSMod.OnQuadPreBuild), [typeof(PQ)]);
        var onQuadBuilt = type.GetMethod(nameof(PQSMod.OnQuadBuilt), [typeof(PQ)]);
        var onVertexBuildHeight = type.GetMethod(nameof(PQSMod.OnVertexBuildHeight));
        var onVertexBuild = type.GetMethod(nameof(PQSMod.OnVertexBuild));

        var overridesQuadPreBuild = onQuadPreBuild.DeclaringType != typeof(PQSMod);
        var overridesQuadBuilt = onQuadBuilt.DeclaringType != typeof(PQSMod);
        var overridesVertexBuildHeight = onVertexBuildHeight.DeclaringType != typeof(PQSMod);
        var overridesVertexBuild = onVertexBuild.DeclaringType != typeof(PQSMod);

        var incompatible =
            overridesQuadPreBuild
            || overridesQuadBuilt
            || overridesVertexBuild
            || overridesVertexBuildHeight;

        // If the PQSMod overrides any of the methods above then it likely needs to
        // have an explicit compatibility shim.
        if (!incompatible)
            return null;

        throw new UnsupportedPQSModException($"PQSMod {type.Name} is not compatible with BatchPQS");
    }

    #endregion
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

public class UnsupportedPQSModException(string message) : Exception(message) { }
