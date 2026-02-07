using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;

namespace BurstPQS.Mod;

[BatchPQSMod(typeof(PQSMod_QuadMeshColliders))]
public class QuadMeshColliders(PQSMod_QuadMeshColliders mod)
    : BatchPQSMod<PQSMod_QuadMeshColliders>(mod)
{
    struct BuildEntry(PQ quad, JobHandle handle)
    {
        public PQ quad = quad;
        public JobHandle handle = handle;
    }

    Coroutine coroutine = null;
    readonly Queue<BuildEntry> entries = [];

    public override void OnQuadBuilt(PQ quad)
    {
        if (quad.subdivision < mod.minLevel)
            return;

        var instanceID = quad.mesh.GetInstanceID();
        var job = new BakeMeshJob(instanceID, false);
        var handle = job.Schedule();

        entries.Enqueue(new(quad, handle));
        coroutine ??= mod.StartCoroutine(CompleteColliderBuilds());
    }

    IEnumerator CompleteColliderBuilds()
    {
        yield return null;

        while (entries.TryDequeue(out var entry))
        {
            try
            {
                entry.handle.Complete();

                var quad = entry.quad;

                if (quad.meshCollider == null)
                    quad.meshCollider = quad.gameObject.AddComponent<MeshCollider>();

                quad.meshCollider.enabled = true;
                quad.meshCollider.sharedMesh = quad.mesh;
                quad.meshCollider.sharedMaterial = mod.physicsMaterial;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        coroutine = null;
    }

    struct BakeMeshJob(int instanceID, bool convex = false) : IJob
    {
        public int instanceID = instanceID;
        public bool convex = convex;

        public readonly void Execute() => Physics.BakeMesh(instanceID, convex);
    }
}
