using System;
using BurstPQS.Util;
using Parallax;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace BurstPQS.ParallaxContinued.Mods;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_Parallax))]
public class Parallax(PQSMod_Parallax mod) : BatchPQSMod<PQSMod_Parallax>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);
        jobSet.Add(new BuildJob(mod));
    }

    public override void OnQuadBuilt(PQ quad)
    {
        base.OnQuadBuilt(quad);

        if (quad.isVisible)
            ScatterComponent.OnQuadVisibleBuilt(quad);
    }

    [BurstCompile]
    struct BuildJob(PQSMod_Parallax mod) : IBatchPQSVertexJob, IBatchPQSMeshBuiltJob, IDisposable
    {
        readonly ObjectHandle<PQSMod_Parallax> mod = new(mod);
        NativeArray<Vector3> uvCache;

        public void BuildVertices(in BuildVerticesData data)
        {
            uvCache = new NativeArray<Vector3>(data.VertexCount, Allocator.Persistent);

            for (int i = 0; i < data.VertexCount; ++i)
                uvCache[i] = new Vector3(
                    (float)data.u[i],
                    (float)data.v[i],
                    data.allowScatter[i] ? 1f : 0f
                );
        }

        public void OnMeshBuilt(PQ quad)
        {
            var mod = this.mod.Target;
            uvCache.CopyTo(mod.uvCache);
        }

        public void Dispose()
        {
            mod.Dispose();
            uvCache.Dispose();
        }
    }
}
