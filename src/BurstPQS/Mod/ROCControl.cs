using System;
using BurstPQS.Util;
using Unity.Burst;

namespace BurstPQS.Mod;

[BurstCompile]
[BatchPQSMod(typeof(PQSROCControl))]
public class ROCControl(PQSROCControl mod) : BatchPQSMod<PQSROCControl>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { mod = new(mod), rocsActive = mod.rocsActive });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob, IBatchPQSMeshBuiltJob, IDisposable
    {
        public ObjectHandle<PQSROCControl> mod;
        public bool rocsActive;
        bool allowROCScatter;

        public void BuildVertices(in BuildVerticesData data)
        {
            allowROCScatter = data.allowScatter[data.VertexCount - 1];
        }

        public void OnMeshBuilt(PQ quad)
        {
            var mod = this.mod.Target;

            mod.allowROCScatter = allowROCScatter;

            // Restore rocsActive so stock OnQuadBuilt sees the value set by
            // this quad's OnQuadPreBuild. When multiple quads are pre-built
            // before completion (e.g. in OnQuadSubdivided), the first completed
            // quad's stock OnQuadBuilt resets rocsActive to false, causing
            // subsequent quads to skip ROC spawning.
            mod.rocsActive = rocsActive;
        }

        public void Dispose()
        {
            mod.Dispose();
        }
    }
}
