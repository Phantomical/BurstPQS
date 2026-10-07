using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace BurstPQS;

// Cooks quad collider meshes on worker threads with Physics.BakeMesh, then
// assigns them at the start of the next FixedUpdate so MeshCollider reuses the
// baked data instead of cooking on the main thread.
//
// Quads are batched so there is at most one bake job in flight. Meshes must
// not be modified while that job runs, so anything that may touch a quad mesh
// calls CompleteBake first.
[BurstCompile]
internal static class QuadColliderBaker
{
    static readonly ProfilerMarker ApplyMarker = new("QuadColliderBaker.Apply");

    struct Entry
    {
        public PQ quad;
        public PQSMod_QuadMeshColliders mod;
    }

    [BurstCompile]
    struct BakeJob : IJobParallelFor
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> meshIds;

        public void Execute(int index) => Physics.BakeMesh(meshIds[index], false);
    }

    static readonly List<Entry> queued = [];
    static readonly List<Entry> baking = [];

    // Quads with a collider waiting to be assigned. Destroyed quads are
    // removed so that stale entries get skipped.
    static readonly HashSet<PQ> pendingQuads = [];

    static JobHandle handle;
    static bool jobRunning;
    static TimingManager registeredWith;

    public static void Enqueue(PQ quad, PQSMod_QuadMeshColliders mod)
    {
        var entry = new Entry { quad = quad, mod = mod };

        if (!EnsureRegistered())
        {
            // No FixedUpdate to apply it in, so do what stock does.
            Apply(entry);
            return;
        }

        if (pendingQuads.Add(quad))
            queued.Add(entry);
    }

    public static void OnQuadDestroy(PQ quad)
    {
        CompleteBake();
        pendingQuads.Remove(quad);
    }

    public static void CompleteBake()
    {
        if (!jobRunning)
            return;

        jobRunning = false;
        handle.Complete();
    }

    public static void Schedule()
    {
        if (jobRunning || baking.Count != 0 || queued.Count == 0)
            return;

        foreach (var entry in queued)
        {
            if (pendingQuads.Contains(entry.quad) && entry.quad != null)
                baking.Add(entry);
        }
        queued.Clear();

        if (baking.Count == 0)
            return;

        var meshIds = new NativeArray<int>(
            baking.Count,
            Allocator.TempJob,
            NativeArrayOptions.UninitializedMemory
        );
        for (int i = 0; i < baking.Count; ++i)
            meshIds[i] = baking[i].quad.mesh.GetInstanceID();

        handle = new BakeJob { meshIds = meshIds }.Schedule(baking.Count, 4);
        jobRunning = true;
        JobHandle.ScheduleBatchedJobs();
    }

    static void OnFixedUpdate()
    {
        using (ApplyMarker.Auto())
        {
            CompleteBake();

            foreach (var entry in baking)
            {
                if (pendingQuads.Remove(entry.quad) && entry.quad != null)
                    Apply(entry);
            }
            baking.Clear();
        }

        // Anything queued while the last batch was in flight goes into the
        // next one.
        Schedule();

        if (baking.Count == 0 && queued.Count == 0)
            Unregister();
    }

    static void Apply(Entry entry)
    {
        var quad = entry.quad;
        if (quad.meshCollider == null)
            quad.meshCollider = quad.gameObject.AddComponent<MeshCollider>();

        quad.meshCollider.enabled = true;
        quad.meshCollider.sharedMesh = quad.mesh;
        quad.meshCollider.sharedMaterial = entry.mod.physicsMaterial;
    }

    static bool EnsureRegistered()
    {
        var instance = TimingManager.Instance;
        if (instance == null)
            return false;

        if (registeredWith == instance)
            return true;

        Unregister();
        TimingManager.FixedUpdateAdd(TimingManager.TimingStage.ObscenelyEarly, OnFixedUpdate);
        registeredWith = instance;
        return true;
    }

    static void Unregister()
    {
        // A destroyed TimingManager took the registration with it.
        if (registeredWith != null)
            TimingManager.FixedUpdateRemove(
                TimingManager.TimingStage.ObscenelyEarly,
                OnFixedUpdate
            );
        registeredWith = null;
    }
}
