using System;
using BurstPQS.Map;
using Parallax.PQS_Mods;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.ParallaxContinued.Mods;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_MapDecalVertexRemoveScatter))]
public class MapDecalVertexRemoveScatter(PQSMod_MapDecalVertexRemoveScatter mod)
    : BatchPQSMod<PQSMod_MapDecalVertexRemoveScatter>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);
        if (mod.debugShowDecal)
            jobSet.Add(new BuildJob(mod));
    }

    [BurstCompile]
    struct BuildJob(PQSMod_MapDecalVertexRemoveScatter mod) : IBatchPQSVertexJob, IDisposable
    {
        public BurstMapSO? debugColorMap = mod.debugColorMap is not null
            ? BurstMapSO.Create(mod.debugColorMap)
            : null;

        public bool quadActive = mod.quadActive;
        public double inclusionAngle = mod.inclusionAngle;
        public Vector3d normalizedPosition = mod.normalisedPosition;
        public Quaternion rot = mod.rot;
        public double radius = mod.radius;

        public void BuildVertices(in BuildVerticesData data)
        {
            if (!quadActive)
            {
                data.vertColor.Clear();
                return;
            }

            var sphere = data.sphere;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                if (sphere.isBuildingMaps)
                {
                    var quadAngle = Math.Acos(
                        Vector3d.Dot(data.directionFromCenter[i], normalizedPosition)
                    );
                    if (quadAngle > inclusionAngle)
                        continue;
                }

                var vertRot = rot * data.directionFromCenter[i];
                var u = (float)((vertRot.x * sphere.radius / radius + 1.0) * 0.5);
                var v = (float)((vertRot.z * sphere.radius / radius + 1.0) * 0.5);

                if (u > 1 || v > 1 || u < 0 || v < 0)
                    continue;

                if (this.debugColorMap is not BurstMapSO debugColorMap)
                    continue;

                var maskValue = debugColorMap.GetPixelColor(u, v).g;
                data.vertColor[i] = maskValue > 0.01f ? Color.green : Color.red;
            }
        }

        public void Dispose()
        {
            debugColorMap?.Dispose();
            debugColorMap = null;
        }
    }
}
