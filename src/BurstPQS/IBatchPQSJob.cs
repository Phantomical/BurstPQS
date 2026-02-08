using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BurstPQS.Collections;
using BurstPQS.Util;
using HarmonyLib;
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

public struct SphereData(PQS sphere)
{
    public double radius = sphere.radius;
    public double radiusMin = sphere.radiusMin;
    public double radiusMax = sphere.radiusMax;
    public bool isBuildingMaps = sphere.isBuildingMaps;

    public readonly double radiusDelta => radiusMax - radiusMin;
}

public unsafe struct BuildHeightsData
{
    public SphereData sphere { get; private set; }

    public int VertexCount
    {
        [return: AssumeRange(0, int.MaxValue)]
        get;
        internal set;
    }

    [NoAlias]
    internal Vector3d* _directionFromCenter;

    [NoAlias]
    readonly double* _u;

    [NoAlias]
    readonly double* _v;

    [NoAlias]
    readonly double* _sx;

    [NoAlias]
    readonly double* _sy;

    [NoAlias]
    readonly double* _longitude;

    [NoAlias]
    readonly double* _latitude;

    [NoAlias]
    readonly double* _height;

    internal BuildHeightsData(SphereData sphere, int vertexCount)
    {
        this.sphere = sphere;
        VertexCount = vertexCount;

        _directionFromCenter = AllocVertexArray<Vector3d>();
        _u = AllocVertexArray<double>();
        _v = AllocVertexArray<double>();
        _sx = AllocVertexArray<double>();
        _sy = AllocVertexArray<double>();
        _longitude = AllocVertexArray<double>();
        _latitude = AllocVertexArray<double>();
        _height = AllocVertexArray<double>();
    }

    internal readonly T* AllocVertexArray<T>()
        where T : unmanaged
    {
        var ptr = (T*)
            UnsafeUtility.Malloc(
                VertexCount * sizeof(T),
                UnsafeUtility.AlignOf<T>(),
                Allocator.Temp
            );
        if (ptr is null)
            throw new OutOfMemoryException("failed to allocate PQS temporary buffer data");
        return ptr;
    }

    public readonly MemorySpan<Vector3d> directionFromCenter =>
        CreateNativeArray(_directionFromCenter);

    public readonly MemorySpan<double> vertHeight => CreateNativeArray(_height);

    public readonly MemorySpan<double> u => CreateNativeArray(_u);
    public readonly MemorySpan<double> v => CreateNativeArray(_v);

    public readonly MemorySpan<double> sx => CreateNativeArray(_sx);
    public readonly MemorySpan<double> sy => CreateNativeArray(_sy);

    public readonly MemorySpan<double> longitude => CreateNativeArray(_longitude);
    public readonly MemorySpan<double> latitude => CreateNativeArray(_latitude);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly MemorySpan<T> CreateNativeArray<T>(T* data)
        where T : unmanaged
    {
        return new MemorySpan<T>(data, VertexCount);
    }
}

public unsafe struct BuildVerticesData
{
    internal BuildHeightsData data;

    public readonly SphereData sphere => data.sphere;

    public readonly int VertexCount => data.VertexCount;

    #region BuildHeightData
    public readonly MemorySpan<Vector3d> directionFromCenter => data.directionFromCenter;

    public readonly MemorySpan<double> vertHeight => data.vertHeight;

    public readonly MemorySpan<double> u => data.u;
    public readonly MemorySpan<double> v => data.v;

    public readonly MemorySpan<double> sx => data.sx;
    public readonly MemorySpan<double> sy => data.sy;

    public readonly MemorySpan<double> longitude => data.longitude;
    public readonly MemorySpan<double> latitude => data.latitude;
    #endregion

    #region BuildVerticesData
    [NoAlias]
    internal Color* _vertColor;

    [NoAlias]
    internal double* _u2;

    [NoAlias]
    internal double* _v2;

    [NoAlias]
    internal double* _u3;

    [NoAlias]
    internal double* _v3;

    [NoAlias]
    internal double* _u4;

    [NoAlias]
    internal double* _v4;

    [NoAlias]
    internal bool* _allowScatter;

    internal BuildVerticesData(BuildHeightsData data)
    {
        this.data = data;

        _vertColor = AllocVertexArray<Color>();
        _u2 = AllocVertexArray<double>();
        _v2 = AllocVertexArray<double>();
        _u3 = AllocVertexArray<double>();
        _v3 = AllocVertexArray<double>();
        _u4 = AllocVertexArray<double>();
        _v4 = AllocVertexArray<double>();
        _allowScatter = AllocVertexArray<bool>();
    }

    internal readonly T* AllocVertexArray<T>()
        where T : unmanaged
    {
        return data.AllocVertexArray<T>();
    }

    public readonly MemorySpan<Color> vertColor => CreateNativeArray(_vertColor);
    public readonly MemorySpan<double> u2 => CreateNativeArray(_u2);
    public readonly MemorySpan<double> u3 => CreateNativeArray(_u3);
    public readonly MemorySpan<double> u4 => CreateNativeArray(_u4);
    public readonly MemorySpan<double> v2 => CreateNativeArray(_v2);
    public readonly MemorySpan<double> v3 => CreateNativeArray(_v3);
    public readonly MemorySpan<double> v4 => CreateNativeArray(_v4);
    public readonly MemorySpan<bool> allowScatter => CreateNativeArray(_allowScatter);
    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly MemorySpan<T> CreateNativeArray<T>(T* data)
        where T : unmanaged
    {
        return new MemorySpan<T>(data, VertexCount);
    }
}

public unsafe struct BuildMeshData
{
    internal BuildVerticesData data;

    public readonly SphereData sphere => data.sphere;

    public readonly int VertexCount => data.VertexCount;

    public double VertMax { get; internal set; }
    public double VertMin { get; internal set; }

    #region BuildHeightData
    public readonly MemorySpan<Vector3d> directionFromCenter => data.directionFromCenter;

    public readonly MemorySpan<double> vertHeight => data.vertHeight;

    public readonly MemorySpan<double> u => data.u;
    public readonly MemorySpan<double> v => data.v;

    public readonly MemorySpan<double> sx => data.sx;
    public readonly MemorySpan<double> sy => data.sy;

    public readonly MemorySpan<double> longitude => data.longitude;
    public readonly MemorySpan<double> latitude => data.latitude;
    #endregion

    #region BuildVerticesData
    public readonly MemorySpan<Color> vertColor => data.vertColor;
    public readonly MemorySpan<double> u2 => data.u2;
    public readonly MemorySpan<double> u3 => data.u3;
    public readonly MemorySpan<double> u4 => data.u4;
    public readonly MemorySpan<double> v2 => data.v2;
    public readonly MemorySpan<double> v3 => data.v3;
    public readonly MemorySpan<double> v4 => data.v4;
    public readonly MemorySpan<bool> allowScatter => data.allowScatter;
    #endregion

    readonly Vector3d* _vertsD;
    readonly Vector3* _verts;
    readonly Vector3* _normals;
    readonly Vector2* _uvs;
    readonly Vector2* _uv2s;
    readonly Vector2* _uv3s;
    readonly Vector2* _uv4s;
    readonly Vector4* _tangents;

    public readonly MemorySpan<Vector3d> vertsD => CreateNativeArray(_vertsD);
    public readonly MemorySpan<Vector3> verts => CreateNativeArray(_verts);
    public readonly MemorySpan<Vector3> normals => CreateNativeArray(_normals);
    public readonly MemorySpan<Vector2> uvs => CreateNativeArray(_uvs);
    public readonly MemorySpan<Vector2> uv2s => CreateNativeArray(_uv2s);
    public readonly MemorySpan<Vector2> uv3s => CreateNativeArray(_uv3s);
    public readonly MemorySpan<Vector2> uv4s => CreateNativeArray(_uv4s);
    public readonly MemorySpan<Vector4> tangents => CreateNativeArray(_tangents);

    internal BuildMeshData(BuildVerticesData data)
    {
        this.data = data;

        _vertsD = AllocVertexArray<Vector3d>();
        _verts = AllocVertexArray<Vector3>();
        _normals = AllocVertexArray<Vector3>();
        _uvs = AllocVertexArray<Vector2>();
        _uv2s = AllocVertexArray<Vector2>();
        _uv3s = AllocVertexArray<Vector2>();
        _uv4s = AllocVertexArray<Vector2>();
        _tangents = AllocVertexArray<Vector4>();
    }

    internal readonly T* AllocVertexArray<T>()
        where T : unmanaged
    {
        return data.AllocVertexArray<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly MemorySpan<T> CreateNativeArray<T>(T* data)
        where T : unmanaged
    {
        return new MemorySpan<T>(data, VertexCount);
    }
}

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
