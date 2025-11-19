using System;
using System.Collections.Generic;
using BurstPQS.Mod;
using UnityEngine;

namespace BurstPQS;

public interface IBatchPQSModV1
{
    void OnBatchVertexBuild(in QuadBuildData data);

    void OnBatchVertexBuildHeight(in QuadBuildData data);
}

public abstract class BatchPQSModV1 : IBatchPQSModV1, IDisposable
{
    public virtual void OnSetup() { }

    public virtual void Dispose() { }

    /// <summary>
    /// A callback, like <see cref="PQSMod.OnVertexBuild"/> except that it gets
    /// called once
    /// </summary>
    /// <param name="data"></param>
    public virtual void OnBatchVertexBuild(in QuadBuildData data) { }

    public virtual void OnBatchVertexBuildHeight(in QuadBuildData data) { }

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

    /// <summary>
    /// Create a <see cref="BatchPQSModV1"/> adapter for a <see cref="PQSMod"/>.
    /// If no adapter is configured it will either create a <see cref="Shim"/>
    /// or return null, if none of <c>OnVertexBuild</c> and <c>OnVertexBuildHeight</c>
    /// is override.
    /// </summary>
    public static BatchPQSModV1 Create(PQSMod mod)
    {
        var type = mod.GetType();

        if (ModTypes.TryGetValue(type, out var batchMod))
            return (BatchPQSModV1)Activator.CreateInstance(batchMod, [mod]);

        if (mod is IBatchPQSModV1 interfaceMod)
            return new InterfaceShim(interfaceMod);

        return Shim.Create(mod);
    }
    #endregion
}

public abstract class BatchPQSModV1<T>(T mod) : BatchPQSModV1
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
