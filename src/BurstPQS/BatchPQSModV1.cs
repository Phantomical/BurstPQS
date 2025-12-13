using System;
using System.Collections.Generic;
using BurstPQS.Mod;
using UnityEngine;

namespace BurstPQS;

public interface IBatchPQSModV1
{
    void OnBatchVertexBuild(in QuadBuildDataV1 data);

    void OnBatchVertexBuildHeight(in QuadBuildDataV1 data);
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
    public virtual void OnBatchVertexBuild(in QuadBuildDataV1 data) { }

    public virtual void OnBatchVertexBuildHeight(in QuadBuildDataV1 data) { }
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
