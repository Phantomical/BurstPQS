using System;
using BurstPQS.Map;
using BurstPQS.Util;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Mod;

// [BurstCompile]
[BatchPQSMod(typeof(PQSMod_RemoveQuadMap))]
public class RemoveQuadMap(PQSMod_RemoveQuadMap mod) : BatchPQSMod<PQSMod_RemoveQuadMap>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);
        mod.OnQuadBuilt(quad);

        jobSet.Add(
            new BuildJob
            {
                mod = mod,
                map = BurstMapSO.Create(mod.map),
                minHeight = mod.minHeight,
                maxHeight = mod.maxHeight,
            }
        );
    }

    struct BuildJob : IBatchPQSVertexJob, IBatchPQSMeshBuiltJob, IDisposable
    {
        public PQSMod_RemoveQuadMap mod;
        public BurstMapSO map;
        public float minHeight;
        public float maxHeight;
        bool quadVisible;

        public void BuildVertices(in BuildVerticesData data)
        {
            quadVisible = false;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var height = map.GetPixelFloat((float)data.u[i], (float)data.v[i]);
                if (height >= minHeight && height <= maxHeight)
                {
                    quadVisible = true;
                    break;
                }
            }
        }

        public void OnMeshBuilt(PQ quad)
        {
            mod.quadVisible = quadVisible;
        }

        public void Dispose()
        {
            map.Dispose();
        }
    }
}
