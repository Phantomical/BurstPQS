using System;
using System.Collections.Generic;
using UnityEngine;

namespace BurstPQS;

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
    /// Called just before the job to build the quad is launched. Anything that
    /// will modify the quad needs to be added to <paramref name="jobSet"/>.
    /// </summary>
    /// <param name="quad"></param>
    /// <param name="jobSet"></param>
    public virtual void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet) { }

    public virtual void OnQuadBuilt(PQ quad) { }

    /// <summary>
    /// Called during PQS teardown.
    /// </summary>
    public virtual void Dispose() { }

    #region Registry
    static readonly Dictionary<Type, Type> ModTypes = [];
    static readonly HashSet<Type> ModShims = [];

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
            throw new ArgumentException($"{mod.Name} does not inherit from PQSMod", nameof(mod));

        var batchPqsModType = typeof(BatchPQSMod<>).MakeGenericType(mod);
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

    public static void RegisterShimmedPQSMod(Type mod)
    {
        ModShims.Add(mod);
    }

    public static BatchPQSMod Create(PQSMod mod)
    {
        var type = mod.GetType();

        if (ModShims.Contains(type))
            return new Mod.Shim(mod);

        if (ModTypes.TryGetValue(type, out var batchMod))
            return (BatchPQSMod)Activator.CreateInstance(batchMod, [mod]);

        var onQuadPreBuild = type.GetMethod(nameof(PQSMod.OnQuadPreBuild), [typeof(PQ)]);
        var onQuadBuilt = type.GetMethod(nameof(PQSMod.OnQuadBuilt), [typeof(PQ)]);
        var onMeshBuild = type.GetMethod(nameof(PQSMod.OnMeshBuild), []);
        var onVertexBuildHeight = type.GetMethod(nameof(PQSMod.OnVertexBuildHeight));
        var onVertexBuild = type.GetMethod(nameof(PQSMod.OnVertexBuild));

        var overridesQuadPreBuild = onQuadPreBuild.DeclaringType != typeof(PQSMod);
        var overridesQuadBuilt = onQuadBuilt.DeclaringType != typeof(PQSMod);
        var overridesMeshBuild = onMeshBuild.DeclaringType != typeof(PQSMod);
        var overridesVertexBuildHeight = onVertexBuildHeight.DeclaringType != typeof(PQSMod);
        var overridesVertexBuild = onVertexBuild.DeclaringType != typeof(PQSMod);

        if (!overridesVertexBuild && !overridesVertexBuildHeight)
        {
            // This is a shim which just passes through OnQuadPreBuild and OnQuadBuilt
            if (overridesQuadBuilt || overridesQuadPreBuild || overridesMeshBuild)
                return new BatchPQSModShim(mod) { overridesMeshBuild = overridesMeshBuild };

            // Otherwise it doesn't override any build methods, so it is likely
            // compatible.
            return null;
        }

        throw new UnsupportedPQSModException($"PQSMod {type.Name} is not compatible with BatchPQS");
    }

    class BatchPQSModShim(PQSMod mod) : BatchPQSMod<PQSMod>(mod)
    {
        public bool overridesMeshBuild;

        public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
        {
            base.OnQuadPreBuild(quad, jobSet);

            if (overridesMeshBuild)
                jobSet.Add(new MeshBuiltCallback(mod));
        }

        readonly struct MeshBuiltCallback(PQSMod mod) : IBatchPQSMeshBuiltJob
        {
            readonly PQSMod mod = mod;

            public void OnMeshBuilt(PQ quad) => mod.OnMeshBuild();
        }
    }
    #endregion
}

public abstract class BatchPQSMod<T>(T mod) : BatchPQSMod
    where T : PQSMod
{
    protected T mod = mod;

    public T Mod => mod;

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet) => mod.OnQuadPreBuild(quad);

    public override void OnQuadBuilt(PQ quad) => mod.OnQuadBuilt(quad);

    public override string ToString()
    {
        if (mod is null)
            return $"null ({GetType().Name})";
        return $"{mod.name}";
    }
}

[Obsolete("Just derive from BatchPQSMod directly")]
public abstract class InlineBatchPQSMod<T>(T mod) : BatchPQSMod<T>(mod)
    where T : PQSMod { }

public class UnsupportedPQSModException(string message) : Exception(message) { }
