using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BurstPQS.Collections;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;

#pragma warning disable IDE1006 // Naming Styles

namespace BurstPQS;

// You can implement build behaviour for a PQSMod by implementing some of the
// traits below on a struct and then registering them with the job set in
// OnQuadPreBuild.
//
// They will be called on the quad job thread and properly burst compiled if
// you annotate the job struct with [BurstCompile].

/// <summary>
/// Called to build the vertex heights for a single quad.
/// </summary>
///
/// <remarks>
/// <para>
/// This fills much the same role as overriding <c>OnBuildHeight</c>
/// in a regular <see cref="PQSMod"/>. The main differences are that
/// it is called with the data for the entire quad at once.
/// </para>
///
/// <para>
/// You can compile this method using burst by annotating your job struct with
/// <c>[BurstCompile]</c>.
/// </para>
/// </remarks>
[JobProducerType(typeof(IBatchPQSJobExtensions.HeightJobStruct<>))]
public interface IBatchPQSHeightJob
{
    void BuildHeights(in BuildHeightsData data);
}

/// <summary>
/// Called to build the other vertex properties for a quad.
/// </summary>
///
/// <remarks>
/// <para>
/// This fills much the same role as overriding <c>OnBuildVertex</c>
/// in a regular <see cref="PQSMod"/>. The main differences are that
/// it is called with the data for the entire quad at once.
/// </para>
///
/// <para>
/// You can compile this method using burst by annotating your job struct with
/// <c>[BurstCompile]</c>.
/// </para>
/// </remarks>
[JobProducerType(typeof(IBatchPQSJobExtensions.VertexJobStruct<>))]
public interface IBatchPQSVertexJob
{
    void BuildVertices(in BuildVerticesData data);
}

/// <summary>
/// Called once the mesh has been built, but before it is assigned to the unity
/// mesh object itself.
/// </summary>
///
/// <remarks>
/// <para>
/// This allows you to modify the final mesh data on the job thread before it
/// gets sent back to the main thread.
/// </para>
///
/// <para>
/// You can compile this method using burst by annotating your job struct with
/// <c>[BurstCompile]</c>.
/// </para>
/// </remarks>
[JobProducerType(typeof(IBatchPQSJobExtensions.MeshJobStruct<>))]
public interface IBatchPQSMeshJob
{
    void BuildMesh(in BuildMeshData data);
}

/// <summary>
/// Called back on the main thread once the job has completed.
/// </summary>
public interface IBatchPQSMeshBuiltJob
{
    void OnMeshBuilt(PQ quad);
}

internal abstract unsafe class JobData : IDisposable
{
    protected delegate void BuildHeightsDelegate(void* self, in BuildHeightsData data);
    protected delegate void BuildVerticesDelegate(void* self, in BuildVerticesData data);
    protected delegate void BuildMeshDelegate(void* self, in BuildMeshData data);
    protected delegate void OnMeshBuiltDelegate(void* self, PQ quad);
    protected delegate void DisposeDelegate(void* self);

    protected JobData() { }

    public abstract void BuildHeights(in BuildHeightsData data);
    public abstract void BuildVertices(in BuildVerticesData data);
    public abstract void BuildMesh(in BuildMeshData data);
    public abstract void OnMeshBuilt(PQ quad);
    public abstract void Dispose();

    protected static void DoDispose<T>(void* job)
        where T : IDisposable
    {
        Unsafe.AsRef<T>(job).Dispose();
    }
}

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
internal sealed unsafe class JobData<T>(in T job) : JobData
    where T : struct
{
    static readonly ProfilerMarker BuildHeightsMarker;
    static readonly ProfilerMarker BuildVerticesMarker;
    static readonly ProfilerMarker BuildMeshMarker;

    static readonly BuildHeightsDelegate BuildHeightsFunc;
    static readonly BuildVerticesDelegate BuildVerticesFunc;
    static readonly BuildMeshDelegate BuildMeshFunc;
    static readonly OnMeshBuiltDelegate OnMeshBuiltFunc;
    static readonly DisposeDelegate DisposeFunc;
    static readonly Action<T> AutoDisposeFunc;

    static JobData()
    {
        var type = typeof(T);
        var profileName = type.IsNested ? type.DeclaringType.Name : type.Name;

        BuildHeightsMarker = new($"{profileName}:{nameof(IBatchPQSHeightJob.BuildHeights)}");
        BuildVerticesMarker = new($"{profileName}:{nameof(IBatchPQSVertexJob.BuildVertices)}");
        BuildMeshMarker = new($"{profileName}:{nameof(IBatchPQSMeshJob.BuildMesh)}");

        if (typeof(IBatchPQSHeightJob).IsAssignableFrom(type))
        {
            var executeFn = typeof(IBatchPQSJobExtensions.HeightJobStruct<>)
                .MakeGenericType(type)
                .GetMethod(nameof(IBatchPQSJobExtensions.HeightJobStruct<>.Execute));
            var executeDel = (BuildHeightsDelegate)
                Delegate.CreateDelegate(typeof(BuildHeightsDelegate), executeFn);

            BuildHeightsFunc = BurstUtil.MaybeCompileFunctionPointer(executeDel).Invoke;
        }

        if (typeof(IBatchPQSVertexJob).IsAssignableFrom(type))
        {
            var executeFn = typeof(IBatchPQSJobExtensions.VertexJobStruct<>)
                .MakeGenericType(type)
                .GetMethod(nameof(IBatchPQSJobExtensions.VertexJobStruct<>.Execute));
            var executeDel = (BuildVerticesDelegate)
                Delegate.CreateDelegate(typeof(BuildVerticesDelegate), executeFn);

            BuildVerticesFunc = BurstUtil.MaybeCompileFunctionPointer(executeDel).Invoke;
        }

        if (typeof(IBatchPQSMeshJob).IsAssignableFrom(type))
        {
            var executeFn = typeof(IBatchPQSJobExtensions.MeshJobStruct<>)
                .MakeGenericType(type)
                .GetMethod(nameof(IBatchPQSJobExtensions.MeshJobStruct<>.Execute));
            var executeDel = (BuildMeshDelegate)
                Delegate.CreateDelegate(typeof(BuildMeshDelegate), executeFn);

            BuildMeshFunc = BurstUtil.MaybeCompileFunctionPointer(executeDel).Invoke;
        }

        if (typeof(IBatchPQSMeshBuiltJob).IsAssignableFrom(type))
        {
            var executeFn = typeof(IBatchPQSJobExtensions.OnMeshBuiltStruct<>)
                .MakeGenericType(type)
                .GetMethod(nameof(IBatchPQSJobExtensions.MeshJobStruct<>.Execute));
            OnMeshBuiltFunc = (OnMeshBuiltDelegate)
                Delegate.CreateDelegate(typeof(OnMeshBuiltDelegate), executeFn);
        }

        if (typeof(IDisposable).IsAssignableFrom(type))
        {
            var disposeFn = typeof(JobData)
                .GetMethod(nameof(DoDispose), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(type);

            DisposeFunc = (DisposeDelegate)
                Delegate.CreateDelegate(typeof(DisposeDelegate), disposeFn);
        }

        AutoDisposeFunc = BuildAutoDisposeDelegate();
    }

    static Action<T> BuildAutoDisposeDelegate()
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        List<Expression> exprs = null;
        var param = Expression.Parameter(typeof(T));

        foreach (var field in typeof(T).GetFields(Flags))
        {
            if (field.GetCustomAttribute<DeallocateOnJobCompletionAttribute>() is null)
                continue;

            var fieldType = field.FieldType;
            if (
                fieldType.GetCustomAttribute<NativeContainerSupportsDeallocateOnJobCompletionAttribute>()
                is null
            )
                continue;

            var bufferField = fieldType.GetField("m_Buffer", Flags);
            var allocatorField = fieldType.GetField("m_AllocatorLabel");

            if (!bufferField.FieldType.IsPointer)
                continue;
            if (allocatorField.FieldType != typeof(Allocator))
                continue;

            var dispose = Expression.Call(
                typeof(UnsafeUtility).GetMethod(nameof(UnsafeUtility.Free)),
                Expression.Field(param, bufferField),
                Expression.Field(param, allocatorField)
            );

            exprs ??= [];
            exprs.Add(dispose);
        }

        if (exprs is null)
            return null;

        var block = Expression.Block(exprs);
        var lambda = Expression.Lambda<Action<T>>(block, param);

        return lambda.Compile();
    }

    const int MaxPoolItems = 128;
    static readonly Stack<JobData<T>> Pool = [];

    public static JobData<T> Create(in T job)
    {
        if (Pool.TryPop(out var data))
        {
            data.job = job;
            return data;
        }

        return new(job);
    }

    T job = job;

    public override void BuildHeights(in BuildHeightsData data)
    {
        if (BuildHeightsFunc is null)
            return;

        using var scope = BuildHeightsMarker.Auto();
        fixed (T* pjob = &job)
        {
            BuildHeightsFunc(Unsafe.AsPointer(ref job), in data);
        }
    }

    public override void BuildVertices(in BuildVerticesData data)
    {
        if (BuildVerticesFunc is null)
            return;

        using var scope = BuildVerticesMarker.Auto();
        fixed (T* pjob = &job)
        {
            BuildVerticesFunc(Unsafe.AsPointer(ref job), in data);
        }
    }

    public override void BuildMesh(in BuildMeshData data)
    {
        if (BuildMeshFunc is null)
            return;

        using var scope = BuildMeshMarker.Auto();
        fixed (T* pjob = &job)
        {
            BuildMeshFunc(Unsafe.AsPointer(ref job), in data);
        }
    }

    public override void OnMeshBuilt(PQ quad)
    {
        fixed (T* pjob = &job)
        {
            OnMeshBuiltFunc?.Invoke(Unsafe.AsPointer(ref job), quad);
        }
    }

    public override void Dispose()
    {
        try
        {
            fixed (T* pjob = &job)
            {
                DisposeFunc?.Invoke(Unsafe.AsPointer(ref job));
                AutoDisposeFunc?.Invoke(job);
            }
        }
        finally
        {
            if (Pool.Count < MaxPoolItems)
            {
                job = default;
                Pool.Push(this);
            }
        }
    }
}
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

internal static unsafe class IBatchPQSJobExtensions
{
    internal struct HeightJobStruct<T>
        where T : struct, IBatchPQSHeightJob
    {
        [BurstCompile]
        public static void Execute([NoAlias] void* self, [NoAlias] in BuildHeightsData data)
        {
            Unsafe.AsRef<T>(self).BuildHeights(in data);
        }
    }

    internal struct VertexJobStruct<T>
        where T : struct, IBatchPQSVertexJob
    {
        [BurstCompile]
        public static void Execute([NoAlias] void* self, [NoAlias] in BuildVerticesData data)
        {
            Unsafe.AsRef<T>(self).BuildVertices(in data);
        }
    }

    internal struct MeshJobStruct<T>
        where T : struct, IBatchPQSMeshJob
    {
        [BurstCompile]
        public static void Execute([NoAlias] void* self, [NoAlias] in BuildMeshData data)
        {
            Unsafe.AsRef<T>(self).BuildMesh(in data);
        }
    }

    internal struct OnMeshBuiltStruct<T>
        where T : struct, IBatchPQSMeshBuiltJob
    {
        [BurstCompile]
        public static void Execute(void* self, PQ quad) => Unsafe.AsRef<T>(self).OnMeshBuilt(quad);
    }
}
