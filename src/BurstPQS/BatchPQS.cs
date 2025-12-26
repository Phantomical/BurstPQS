using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BurstPQS.Async;
using BurstPQS.Collections;
using BurstPQS.Patches;
using BurstPQS.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using static PQS;

namespace BurstPQS;

[BurstCompile]
public class BatchPQS : MonoBehaviour
{
    static bool ForceFallback = true;
    static readonly ProfilerMarker BuildQuadMarker = new("BatchPQS.BuildQuad");
    static readonly ProfilerMarker BuildQuadAsyncMarker = new("BatchPQS.BuildQuadAsync");
    static readonly ProfilerMarker UpdateQuadsMarker = new("BatchPQS.UpdateQuads");
    static readonly ProfilerMarker UpdateQuadsInitMarker = new("BatchPQS.UpdateQuadsInit");

    private PQS pqs;
    private BatchPQSMod[] mods;

    // Are there unsupported mods and do we need to fall back to the stock
    // implementation?
    private bool fallback = false;

    void Awake()
    {
        pqs = GetComponent<PQS>();
    }

    void OnDestroy()
    {
        foreach (var mod in mods)
        {
            try
            {
                mod.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    async ValueTask<bool> BuildQuadAsync(PQ quad)
    {
        using var scope = BuildQuadAsyncMarker.Auto();

        if (fallback || ForceFallback)
            return PQS_RevPatch.BuildQuad(pqs, quad);

        if (quad.isBuilt)
            return false;
        if (quad.isSubdivided)
            return false;

        if (quad == null || quad.gameObject == null)
            return false;

        pqs.buildQuad = quad;
        // pqs.Mod_OnQuadPreBuild(quad);

        using var data = new QuadBuildData(pqs, PQS.cacheVertCount);
        using CacheData cache = new(data.VertexCount);
        using var minmax = new NativeArray<double>(2, Allocator.TempJob);

        var vbData = PQS.vbData;
        vbData.buildQuad = quad;
        vbData.allowScatter = true;
        vbData.gnomonicPlane = quad.plane;
        quad.meshVertMax = double.MaxValue;
        quad.meshVertMin = double.MinValue;

        var states = new List<IBatchPQSModState>(mods.Length);
        using var sguard = new StateDisposer(states);
        foreach (var mod in mods)
        {
            var state = mod.OnQuadPreBuild(data);
            if (state is not null)
                states.Add(state);
        }

        var initJob = new InitBuildDataJob
        {
            quadMatrix = quad.quadMatrix,
            data = data.burst,
            reqVertexMapCoords = pqs.reqVertexMapCoods,
            cacheSideVertCount = PQS.cacheSideVertCount,
            cacheMeshSize = PQS.cacheMeshSize,
        };

        var handle = initJob.Schedule();

        foreach (var state in states)
            handle = state.ScheduleBuildHeights(data, handle);
        foreach (var state in states)
            handle = state.ScheduleBuildVertices(data, handle);

        var buildJob = new BuildVerticesJob
        {
            data = data.burst,
            cache = cache,
            minmax = minmax,

            pqsTransform = transform.localToWorldMatrix,
            inverseQuadTransform = data.buildQuad.transform.worldToLocalMatrix,

            surfaceRelativeQuads = pqs.surfaceRelativeQuads,
            reqCustomNormals = pqs.reqCustomNormals,
            reqSphereUV = pqs.reqSphereUV,
            reqUVQuad = pqs.reqUVQuad,
            reqUV2 = pqs.reqUV2,
            reqUV3 = pqs.reqUV3,
            reqUV4 = pqs.reqUV4,

            uvSW = quad.uvSW,
            uvDelta = quad.uvDelta,
            cacheSideVertCount = PQS.cacheSideVertCount,

            radius = pqs.radius,
            meshVertMax = quad.meshVertMax,
            meshVertMin = quad.meshVertMin,
        };

        handle = buildJob.Schedule(handle);

        using (new SuspendProfileScope(scope))
            await JobSynchronizationContext.WaitForJob(handle);

        CopyGeneratedData(data, cache);
        pqs.buildQuad = quad;
        pqs.meshVertMin = minmax[0];
        pqs.meshVertMax = minmax[1];

        quad.mesh.vertices = quad.verts;
        quad.mesh.triangles = PQS.cacheIndices[0];
        quad.mesh.RecalculateBounds();
        quad.edgeState = PQS.EdgeState.Reset;
        pqs.Mod_OnMeshBuild();
        foreach (var state in states)
            state.OnQuadBuilt(data);
        pqs.buildQuad = null;

        return true;
    }

    internal async void UpdateQuadsInit()
    {
        using var scope = UpdateQuadsInitMarker.Auto();

        if (fallback || ForceFallback)
        {
            PQS_RevPatch.UpdateQuadsInit(pqs);
            return;
        }

        pqs.CreateQuads();
        pqs.isThinking = true;
        pqs.quadAllowBuild = false;

        int lastQuadCount;
        for (int iter = 0; iter < 10; iter = ((lastQuadCount >= pqs.quadCount) ? (iter + 1) : 0))
        {
            lastQuadCount = pqs.quadCount;
            for (int num4 = 5; num4 >= 0; num4--)
                pqs.quads[num4].UpdateSubdivisionInit();
        }

        pqs.quadAllowBuild = true;
        var tasks = new ValueTask[6];
        for (int i = 0; i < 6; ++i)
            tasks[i] = UpdateSubdivision(pqs.quads[i]);
        await ValueTask.WhenAll(tasks);

        pqs.isThinking = false;
    }

    internal async void UpdateQuads()
    {
        using var scope = UpdateQuadsMarker.Auto();

        if (fallback || ForceFallback)
        {
            PQS_RevPatch.UpdateQuads(pqs);
            return;
        }

        if (pqs.quads == null)
            return;

        pqs.isThinking = true;
        pqs.maxFrameEnd = Time.realtimeSinceStartup + pqs.maxFrameTime / 3;

        for (int i = 1; i < pqs.quads.Length; ++i)
        {
            var quad = pqs.quads[i];
            var prev = i - 1;

            while (prev >= 0)
            {
                if (
                    (
                        pqs.relativeTargetPosition - pqs.quads[prev].positionPlanetRelative
                    ).sqrMagnitude
                    <= (pqs.relativeTargetPosition - quad.positionPlanetRelative).sqrMagnitude
                )
                    break;

                pqs.quads[prev + 1] = pqs.quads[prev];
                prev--;
            }

            pqs.quads[prev + 1] = quad;
        }

        var count = Mathf.Min(pqs.quads.Length, 5);
        var tasks = new ValueTask[count];
        for (int i = 0; i < count; ++i)
            tasks[i] = UpdateSubdivision(pqs.quads[i]);

        await ValueTask.WhenAll(tasks);

        if (pqs.reqCustomNormals)
            await UpdateEdges();

        pqs.isThinking = false;
    }

    public bool BuildQuad(PQ quad)
    {
        using var scope = BuildQuadMarker.Auto();

        if (fallback || ForceFallback)
            return PQS_RevPatch.BuildQuad(pqs, quad);

        if (quad.isBuilt)
            return false;
        if (quad.isSubdivided)
            return false;

        if (quad == null || quad.gameObject == null)
            return false;

        pqs.buildQuad = quad;
        // pqs.Mod_OnQuadPreBuild(quad);

        using var data = new QuadBuildData(pqs, PQS.cacheVertCount);
        using CacheData cache = new(data.VertexCount);
        using var minmax = new NativeArray<double>(2, Allocator.TempJob);

        var vbData = PQS.vbData;
        vbData.buildQuad = quad;
        vbData.allowScatter = true;
        vbData.gnomonicPlane = quad.plane;
        quad.meshVertMax = double.MaxValue;
        quad.meshVertMin = double.MinValue;

        var states = new List<IBatchPQSModState>(mods.Length);
        using var sguard = new StateDisposer(states);
        foreach (var mod in mods)
        {
            var state = mod.OnQuadPreBuild(data);
            if (state is not null)
                states.Add(state);
        }

        var initJob = new InitBuildDataJob
        {
            quadMatrix = quad.quadMatrix,
            data = data.burst,
            reqVertexMapCoords = pqs.reqVertexMapCoods,
            cacheSideVertCount = PQS.cacheSideVertCount,
            cacheMeshSize = PQS.cacheMeshSize,
        };

        var handle = initJob.Schedule();
        JobHandle.ScheduleBatchedJobs();

        foreach (var state in states)
            handle = state.ScheduleBuildHeights(data, handle);
        foreach (var state in states)
            handle = state.ScheduleBuildVertices(data, handle);
        JobHandle.ScheduleBatchedJobs();

        var buildJob = new BuildVerticesJob
        {
            data = data.burst,
            cache = cache,
            minmax = minmax,

            pqsTransform = transform.localToWorldMatrix,
            inverseQuadTransform = data.buildQuad.transform.worldToLocalMatrix,

            surfaceRelativeQuads = pqs.surfaceRelativeQuads,
            reqCustomNormals = pqs.reqCustomNormals,
            reqSphereUV = pqs.reqSphereUV,
            reqUVQuad = pqs.reqUVQuad,
            reqUV2 = pqs.reqUV2,
            reqUV3 = pqs.reqUV3,
            reqUV4 = pqs.reqUV4,

            uvSW = quad.uvSW,
            uvDelta = quad.uvDelta,
            cacheSideVertCount = PQS.cacheSideVertCount,

            radius = pqs.radius,
            meshVertMax = quad.meshVertMax,
            meshVertMin = quad.meshVertMin,
        };

        buildJob.Schedule(handle).Complete();

        CopyGeneratedData(data, cache);
        pqs.meshVertMin = minmax[0];
        pqs.meshVertMax = minmax[1];

        quad.mesh.vertices = quad.verts;
        quad.mesh.triangles = PQS.cacheIndices[0];
        quad.mesh.RecalculateBounds();
        quad.edgeState = PQS.EdgeState.Reset;
        pqs.Mod_OnMeshBuild();
        foreach (var state in states)
            state.OnQuadBuilt(data);
        pqs.buildQuad = null;
        return true;
    }

    void CopyGeneratedData(QuadBuildData data, CacheData cache)
    {
        CopyTo(PQS.verts, cache.verts);
        CopyTo(data.buildQuad.verts, cache.quadVerts);

        if (!pqs.reqCustomNormals)
            CopyTo(PQS.normals, cache.normals);
        if (pqs.reqColorChannel)
            CopyTo(PQS.cacheColors, data.vertColor);
        if (pqs.reqSphereUV || pqs.reqUVQuad)
            CopyTo(PQS.uvs, cache.uvs);
        if (pqs.reqUV2)
            CopyTo(PQS.cacheUV2s, cache.cacheUV2s);
        if (pqs.reqUV3)
            CopyTo(PQS.cacheUV3s, cache.cacheUV3s);
        if (pqs.reqUV4)
            CopyTo(PQS.cacheUV4s, cache.cacheUV4s);
    }

    static void CopyTo<T>(T[] dst, MemorySpan<T> src)
        where T : unmanaged
    {
        if (dst.Length != src.Length)
            throw new IndexOutOfRangeException("src and dst did not have the same length");

        Unsafe.CopyBlock(
            ref Unsafe.As<T, byte>(ref dst[0]),
            ref Unsafe.As<T, byte>(ref src[0]),
            (uint)dst.Length * (uint)Unsafe.SizeOf<T>()
        );
    }

    #region Async Quad Methods
    async ValueTask BuildAsync(PQ quad)
    {
        if (quad.isBuilt || !quad.isActive || !pqs.quadAllowBuild || quad.isCached)
            return;

        if (quad.isSubdivided)
        {
            var tasks = new FixedArray4<ValueTask>();
            for (int i = 0; i < 4; ++i)
                tasks[i] = BuildAsync(quad.subNodes[i]);

            await ValueTask.WhenAll(tasks);
        }
        else
        {
            quad.isBuilt = await BuildQuadAsync(quad);
            if (quad.isBuilt)
                quad.QueueForNormalUpdate();
        }
    }

    async ValueTask UpdateSubdivisionInit(PQ quad)
    {
        quad.UpdateSubdivisionInit();

        if (quad.isSubdivided)
        {
            if (quad.subdivision > pqs.minLevel)
            {
                if (
                    quad.subdivision > pqs.maxLevel
                    || quad.gcDist
                        > pqs.collapseThresholds[quad.subdivision] * quad.subdivideThresholdFactor
                )
                {
                    quad.Collapse();
                    return;
                }
            }

            var tasks = new FixedArray4<ValueTask>();
            for (int i = 0; i < 4; ++i)
            {
                if (quad.subNodes[i])
                    tasks[i] = UpdateSubdivisionInit(quad.subNodes[i]);
                else
                    tasks[i] = ValueTask.CompletedTask;
            }

            await ValueTask.WhenAll(tasks);
        }
        else if (
            quad.gcDist
                < pqs.subdivisionThresholds[quad.subdivision] * quad.subdivideThresholdFactor
            && quad.subdivision < pqs.maxLevel
        )
        {
            await Subdivide(quad);
        }
        else
        {
            quad.onUpdate?.Invoke(quad);
        }
    }

    async ValueTask UpdateSubdivision(PQ quad)
    {
        quad.UpdateTargetRelativity();
        quad.outOfTime = Time.realtimeSinceStartup > pqs.maxFrameEnd;
        // quad.outOfTime = false;

        var subdivision = quad.subdivision;
        var sphereRoot = pqs;

        if (quad.isSubdivided)
        {
            quad.meshRenderer.enabled = false;
            bool flag =
                quad.gcDist > pqs.collapseThresholds[subdivision] * quad.subdivideThresholdFactor;

            if (quad.subdivision <= pqs.maxLevel && (!flag || quad.outOfTime))
            {
                if (flag)
                    quad.isPendingCollapse = true;

                var tasks = new FixedArray4<ValueTask>();
                for (int i = 0; i < 4; ++i)
                {
                    if (quad.subNodes[i])
                        tasks[i] = UpdateSubdivision(quad.subNodes[i]);
                    else
                        tasks[i] = ValueTask.CompletedTask;
                }

                foreach (var task in tasks)
                    await task;
            }
            else if (!quad.Collapse())
            {
                var tasks = new FixedArray4<ValueTask>();
                for (int i = 0; i < 4; ++i)
                {
                    if (quad.subNodes[i])
                        tasks[i] = UpdateSubdivision(quad.subNodes[i]);
                    else
                        tasks[i] = ValueTask.CompletedTask;
                }

                foreach (var task in tasks)
                    await task;
            }
        }
        else if (
            subdivision >= sphereRoot.minLevel
            && (
                !(
                    quad.gcDist
                    < sphereRoot.subdivisionThresholds[subdivision] * quad.subdivideThresholdFactor
                )
                || subdivision >= sphereRoot.maxLevelAtCurrentTgtSpeed
                || quad.outOfTime
            )
        )
        {
            await UpdateVisibility(quad);
        }
        else
        {
            await Subdivide(quad);
        }

        quad.onUpdate?.Invoke(quad);
    }

    async ValueTask UpdateEdges()
    {
        var cache = new Dictionary<PQ, ValueTask>();
        var tasks = new Queue<ValueTask>();

        while (true)
        {
            int count;
            while ((count = pqs.normalUpdateList.Count) > 0)
            {
                count -= 1;
                var quad = pqs.normalUpdateList[count];
                pqs.normalUpdateList.RemoveAt(count);

                if (quad == null)
                    continue;

                if (quad.isSubdivided && quad.isVisible)
                    tasks.Enqueue(UpdateEdgeNormals(quad, cache));
            }

            if (!tasks.TryDequeue(out var task))
                break;

            await task;
        }
    }

    async ValueTask UpdateEdgeNormalsWrap(PQ q, Dictionary<PQ, ValueTask> cache)
    {
        await UpdateEdgeNormals(q, cache);
        q.isQueuedForNormalUpdate = false;
        q.isQueuedOnlyForCornerNormalUpdate = false;
    }

    async ValueTask UpdateEdgeNormals(PQ q, Dictionary<PQ, ValueTask> cache)
    {
        if (
            q == null
            || !q.isActive
            || (q.parent != null && !q.parent.isSubdivided)
            || q.parent == null
        )
            return;

        if (!q.isBuilt)
        {
            if (!cache.TryGetValue(q, out var task))
                return;

            await task;
        }

        bool updateEdgeNormals = !q.isQueuedOnlyForCornerNormalUpdate;
        Vector3 vector = q.edgeNormals[0][0];
        Vector3 vector2 = q.edgeNormals[0][cacheRes];
        Vector3 vector3 = q.edgeNormals[1][0];
        Vector3 vector4 = q.edgeNormals[1][cacheRes];
        Vector3 zero;
        bool flag2 = false;
        bool flag3 = false;
        bool flag4 = false;
        bool flag5 = false;

        if (q.north.subdivision == q.subdivision)
        {
            if (q.north.isSubdivided)
            {
                q.north.GetEdgeQuads(q, out var left, out var right);
                if (left != null && right != null)
                {
                    var leftEdge = left.GetEdge(q);
                    var rightEdge = right.GetEdge(q);
                    if (leftEdge == QuadEdge.Null || rightEdge == QuadEdge.Null)
                        return;

                    await ValueTask.WhenAll(
                        BuildAsyncCached(left, cache),
                        BuildAsyncCached(right, cache)
                    );

                    zero = q.edgeNormals[0][cacheResDiv2];
                    if (right.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(0, 0),
                                1,
                                q.edgeNormals[0],
                                0,
                                1,
                                right.edgeNormals[(int)rightEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector += right.edgeNormals[(int)rightEdge][cacheRes];
                        zero += right.edgeNormals[(int)rightEdge][0];
                    }
                    if (left.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(cacheResDiv2, 0),
                                1,
                                q.edgeNormals[0],
                                cacheResDiv2,
                                1,
                                left.edgeNormals[(int)leftEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector2 += left.edgeNormals[(int)leftEdge][0];
                        zero += left.edgeNormals[(int)leftEdge][cacheRes];
                    }
                    q.vertNormals[vi(cacheResDiv2, 0)] = zero.normalized;
                }
            }
            else
            {
                var leftEdge = q.north.GetEdge(q);
                if (leftEdge == QuadEdge.Null)
                    return;

                if (!q.north.isBuilt)
                    await BuildAsyncCached(q.north, cache);

                if (q.north.isBuilt)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheSideVertCount,
                            vi(0, 0),
                            1,
                            q.edgeNormals[0],
                            0,
                            1,
                            q.north.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector += q.north.edgeNormals[(int)leftEdge][cacheRes];
                    vector2 += q.north.edgeNormals[(int)leftEdge][0];
                }
            }
        }
        else if (q.north.subdivision < q.subdivision)
        {
            q.parent.GetEdgeQuads(q.north, out var left, out var right);
            var leftEdge = q.north.GetEdge(q.parent);
            if (leftEdge == QuadEdge.Null)
                return;

            if (!q.north.isBuilt)
                await BuildAsyncCached(q.north, cache);

            if (q.north.isBuilt)
            {
                if (left == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(0, 0),
                            2,
                            q.edgeNormals[0],
                            0,
                            2,
                            q.north.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector += q.north.edgeNormals[(int)leftEdge][cacheRes];
                    vector2 += q.north.edgeNormals[(int)leftEdge][cacheResDiv2];
                    flag3 = true;
                }
                else if (right == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(0, 0),
                            2,
                            q.edgeNormals[0],
                            0,
                            2,
                            q.north.edgeNormals[(int)leftEdge],
                            cacheResDiv2,
                            -1
                        );
                    }
                    vector += q.north.edgeNormals[(int)leftEdge][cacheResDiv2];
                    vector2 += q.north.edgeNormals[(int)leftEdge][0];
                    flag2 = true;
                }
            }
        }

        if (q.south.subdivision == q.subdivision)
        {
            if (q.south.isSubdivided)
            {
                q.south.GetEdgeQuads(q, out var left, out var right);
                if (left != null && right != null)
                {
                    var leftEdge = left.GetEdge(q);
                    var rightEdge = right.GetEdge(q);
                    if (leftEdge == QuadEdge.Null || rightEdge == QuadEdge.Null)
                        return;

                    await ValueTask.WhenAll(
                        BuildAsyncCached(left, cache),
                        BuildAsyncCached(right, cache)
                    );

                    zero = q.edgeNormals[1][cacheResDiv2];
                    if (right.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(cacheRes, cacheRes),
                                -1,
                                q.edgeNormals[1],
                                0,
                                1,
                                right.edgeNormals[(int)rightEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector3 += right.edgeNormals[(int)rightEdge][cacheRes];
                        zero += right.edgeNormals[(int)rightEdge][0];
                    }
                    if (left.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(cacheResDiv2, cacheRes),
                                -1,
                                q.edgeNormals[1],
                                cacheResDiv2,
                                1,
                                left.edgeNormals[(int)leftEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector4 += left.edgeNormals[(int)leftEdge][0];
                        zero += left.edgeNormals[(int)leftEdge][cacheRes];
                    }
                    q.vertNormals[vi(cacheResDiv2, cacheRes)] = zero.normalized;
                }
            }
            else
            {
                var leftEdge = q.south.GetEdge(q);
                if (leftEdge == QuadEdge.Null)
                    return;

                if (!q.south.isBuilt)
                    await BuildAsyncCached(q.south, cache);

                if (q.south.isBuilt)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheSideVertCount,
                            vi(cacheRes, cacheRes),
                            -1,
                            q.edgeNormals[1],
                            0,
                            1,
                            q.south.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector3 += q.south.edgeNormals[(int)leftEdge][cacheRes];
                    vector4 += q.south.edgeNormals[(int)leftEdge][0];
                }
            }
        }
        else if (q.south.subdivision < q.subdivision)
        {
            q.parent.GetEdgeQuads(q.south, out var left, out var right);
            var leftEdge = q.south.GetEdge(q.parent);
            if (leftEdge == QuadEdge.Null)
                return;

            if (!q.south.isBuilt)
                await BuildAsyncCached(q.south, cache);

            if (q.south.isBuilt)
            {
                if (left == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(cacheRes, cacheRes),
                            -2,
                            q.edgeNormals[1],
                            0,
                            2,
                            q.south.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector3 += q.south.edgeNormals[(int)leftEdge][cacheRes];
                    vector4 += q.south.edgeNormals[(int)leftEdge][cacheResDiv2];
                    flag5 = true;
                }
                else if (right == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(cacheRes, cacheRes),
                            -2,
                            q.edgeNormals[1],
                            0,
                            2,
                            q.south.edgeNormals[(int)leftEdge],
                            cacheResDiv2,
                            -1
                        );
                    }
                    vector3 += q.south.edgeNormals[(int)leftEdge][cacheResDiv2];
                    vector4 += q.south.edgeNormals[(int)leftEdge][0];
                    flag4 = true;
                }
            }
        }

        if (q.east.subdivision == q.subdivision)
        {
            if (q.east.isSubdivided)
            {
                q.east.GetEdgeQuads(q, out var left, out var right);
                if (left != null && right != null)
                {
                    var leftEdge = left.GetEdge(q);
                    var rightEdge = right.GetEdge(q);
                    if (leftEdge == QuadEdge.Null || rightEdge == QuadEdge.Null)
                        return;

                    await ValueTask.WhenAll(
                        BuildAsyncCached(left, cache),
                        BuildAsyncCached(right, cache)
                    );

                    zero = q.edgeNormals[2][cacheResDiv2];
                    if (right.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(0, cacheRes),
                                -cacheSideVertCount,
                                q.edgeNormals[2],
                                0,
                                1,
                                right.edgeNormals[(int)rightEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector4 += right.edgeNormals[(int)rightEdge][cacheRes];
                        zero += right.edgeNormals[(int)rightEdge][0];
                    }
                    if (left.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(0, cacheResDiv2),
                                -cacheSideVertCount,
                                q.edgeNormals[2],
                                cacheResDiv2,
                                1,
                                left.edgeNormals[(int)leftEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector += left.edgeNormals[(int)leftEdge][0];
                        zero += left.edgeNormals[(int)leftEdge][cacheRes];
                    }
                    q.vertNormals[vi(0, cacheResDiv2)] = zero.normalized;
                }
            }
            else
            {
                var leftEdge = q.east.GetEdge(q);
                if (leftEdge == QuadEdge.Null)
                    return;
                if (!q.east.isBuilt)
                    await BuildAsyncCached(q.east, cache);

                if (q.east.isBuilt)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheSideVertCount,
                            vi(0, cacheRes),
                            -cacheSideVertCount,
                            q.edgeNormals[2],
                            0,
                            1,
                            q.east.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector += q.east.edgeNormals[(int)leftEdge][0];
                    vector4 += q.east.edgeNormals[(int)leftEdge][cacheRes];
                }
            }
        }
        else if (q.east.subdivision < q.subdivision)
        {
            q.parent.GetEdgeQuads(q.east, out var left, out var right);
            var leftEdge = q.east.GetEdge(q.parent);
            if (leftEdge == QuadEdge.Null)
                return;

            if (!q.east.isBuilt)
                await BuildAsyncCached(q.east, cache);

            if (q.east.isBuilt)
            {
                if (right == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(0, cacheRes),
                            -(cacheSideVertCount * 2),
                            q.edgeNormals[2],
                            0,
                            2,
                            q.east.edgeNormals[(int)leftEdge],
                            cacheResDiv2,
                            -1
                        );
                    }
                    vector += q.east.edgeNormals[(int)leftEdge][0];
                    vector4 += q.east.edgeNormals[(int)leftEdge][cacheResDiv2];
                    flag5 = true;
                }
                else if (left == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(0, cacheRes),
                            -(cacheSideVertCount * 2),
                            q.edgeNormals[2],
                            0,
                            2,
                            q.east.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector += q.east.edgeNormals[(int)leftEdge][cacheResDiv2];
                    vector4 += q.east.edgeNormals[(int)leftEdge][cacheRes];
                    flag2 = true;
                }
            }
        }

        if (q.west.subdivision == q.subdivision)
        {
            if (q.west.isSubdivided)
            {
                q.west.GetEdgeQuads(q, out var left, out var right);
                if (left != null && right != null)
                {
                    var leftEdge = left.GetEdge(q);
                    var rightEdge = right.GetEdge(q);
                    if (leftEdge == QuadEdge.Null || rightEdge == QuadEdge.Null)
                        return;

                    await ValueTask.WhenAll(
                        BuildAsyncCached(left, cache),
                        BuildAsyncCached(right, cache)
                    );

                    zero = q.edgeNormals[3][cacheResDiv2];
                    if (right.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(cacheRes, 0),
                                cacheSideVertCount,
                                q.edgeNormals[3],
                                0,
                                1,
                                right.edgeNormals[(int)rightEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector2 += right.edgeNormals[(int)rightEdge][cacheRes];
                        zero += right.edgeNormals[(int)rightEdge][0];
                    }

                    if (left.isBuilt)
                    {
                        if (updateEdgeNormals)
                        {
                            pqs.CombineEdgeNormals(
                                q,
                                cacheResDiv2Plus1,
                                vi(cacheRes, cacheResDiv2),
                                cacheSideVertCount,
                                q.edgeNormals[3],
                                cacheResDiv2,
                                1,
                                left.edgeNormals[(int)leftEdge],
                                cacheRes,
                                -2
                            );
                        }
                        vector3 += left.edgeNormals[(int)leftEdge][0];
                        zero += left.edgeNormals[(int)leftEdge][cacheRes];
                    }
                    q.vertNormals[vi(cacheRes, cacheResDiv2)] = zero.normalized;
                }
            }
            else
            {
                var leftEdge = q.west.GetEdge(q);
                if (leftEdge == QuadEdge.Null)
                    return;

                if (!q.west.isBuilt)
                    await BuildAsyncCached(q.west, cache);

                if (q.west.isBuilt)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheSideVertCount,
                            vi(cacheRes, 0),
                            cacheSideVertCount,
                            q.edgeNormals[3],
                            0,
                            1,
                            q.west.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector2 += q.west.edgeNormals[(int)leftEdge][cacheRes];
                    vector3 += q.west.edgeNormals[(int)leftEdge][0];
                }
            }
        }
        else if (q.west.subdivision < q.subdivision)
        {
            q.parent.GetEdgeQuads(q.west, out var left, out var right);
            var leftEdge = q.west.GetEdge(q.parent);
            if (!q.west.isBuilt)
                await BuildAsyncCached(q.west, cache);

            if (q.west.isBuilt)
            {
                if (left == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(cacheRes, 0),
                            cacheSideVertCount * 2,
                            q.edgeNormals[3],
                            0,
                            2,
                            q.west.edgeNormals[(int)leftEdge],
                            cacheRes,
                            -1
                        );
                    }
                    vector2 += q.west.edgeNormals[(int)leftEdge][cacheRes];
                    vector3 += q.west.edgeNormals[(int)leftEdge][cacheResDiv2];
                    flag4 = true;
                }
                else if (right == q)
                {
                    if (updateEdgeNormals)
                    {
                        pqs.CombineEdgeNormals(
                            q,
                            cacheResDiv2Plus1,
                            vi(cacheRes, 0),
                            cacheSideVertCount * 2,
                            q.edgeNormals[3],
                            0,
                            2,
                            q.west.edgeNormals[(int)leftEdge],
                            cacheResDiv2,
                            -1
                        );
                    }
                    vector2 += q.west.edgeNormals[(int)leftEdge][cacheResDiv2];
                    vector3 += q.west.edgeNormals[(int)leftEdge][0];
                    flag3 = true;
                }
            }
        }

        if (!flag2)
            vector += pqs.GetRightmostCornerNormal(q, q.north);
        if (!flag3)
            vector2 += pqs.GetRightmostCornerNormal(q, q.west);
        if (!flag4)
            vector3 += pqs.GetRightmostCornerNormal(q, q.south);
        if (!flag5)
            vector4 += pqs.GetRightmostCornerNormal(q, q.east);

        q.vertNormals[vi(0, 0)] = vector.normalized;
        q.vertNormals[vi(cacheRes, 0)] = vector2.normalized;
        q.vertNormals[vi(cacheRes, cacheRes)] = vector3.normalized;
        q.vertNormals[vi(0, cacheRes)] = vector4.normalized;
        q.mesh.normals = q.vertNormals;
        pqs.Mod_OnQuadUpdateNormals(q);
        if (pqs.reqBuildTangents)
        {
            BuildTangents(q);
        }
    }

    ValueTask BuildAsyncCached(PQ quad, Dictionary<PQ, ValueTask> cache)
    {
        if (quad.isBuilt)
            return ValueTask.CompletedTask;
        if (cache.TryGetValue(quad, out var task))
            return task;
        task = BuildAsync(quad);
        cache.Add(quad, task);
        return task;
    }

    async ValueTask SetVisible(PQ quad)
    {
        if (quad.isVisible)
            return;

        quad.isVisible = true;
        if (!quad.isBuilt)
            await BuildAsync(quad);
        quad.meshRenderer.enabled = !quad.isForcedInvisible;
        quad.onVisible?.Invoke(quad);
    }

    async ValueTask<bool> Subdivide(PQ quad)
    {
        var north = quad.north;
        var south = quad.south;
        var east = quad.east;
        var west = quad.west;
        var subdivision = quad.subdivision;
        var sphereRoot = pqs;
        var subNodes = quad.subNodes;

        if (north.subdivision < subdivision)
            return false;
        if (east.subdivision < subdivision)
            return false;
        if (south.subdivision < subdivision)
            return false;
        if (west.subdivision < subdivision)
            return false;

        if (quad.isSubdivided)
            return true;
        if (!quad.isActive)
            return false;

        for (int i = 0; i < 4; ++i)
        {
            if (subNodes[i] != null)
            {
                subNodes[i].isActive = true;
                Debug.Log(quad.subNodes[i].gameObject.name);
                Debug.Break();
                continue;
            }

            PQ subnode = sphereRoot.AssignQuad(subdivision + 1);
            int num = i % 2;
            int num2 = i / 2;
            subnode.scalePlaneRelative = quad.scalePlaneRelative * 0.5;
            subnode.scalePlanetRelative = sphereRoot.radius * subnode.scalePlaneRelative;
            subnode.quadRoot = quad.quadRoot ?? quad;
            subnode.CreateParent = quad;
            subnode.positionParentRelative =
                subnode.quadRoot.planeRotation
                * new Vector3d(
                    ((double)num - 0.5) * quad.scalePlaneRelative,
                    0.0,
                    ((double)num2 - 0.5) * quad.scalePlaneRelative
                );
            subnode.positionPlanePosition =
                quad.positionPlanePosition + subnode.positionParentRelative;
            subnode.positionPlanetRelative = subnode.positionPlanePosition.normalized;
            subnode.positionPlanet =
                subnode.positionPlanetRelative
                * sphereRoot.GetSurfaceHeight(subnode.positionPlanetRelative);
            subnode.plane = quad.plane;
            subnode.sphereRoot = sphereRoot;
            subnode.subdivision = subdivision + 1;
            subnode.parent = quad;
            subnode.Corner = i;
            subnode.name = base.gameObject.name + i;
            subnode.gameObject.layer = base.gameObject.layer;
            sphereRoot.QuadCreated(subnode);
            quad.subNodes[i] = subnode;
        }

        subNodes[0].north = subNodes[2];
        subNodes[0].east = subNodes[1];
        subNodes[1].north = subNodes[3];
        subNodes[1].west = subNodes[0];
        subNodes[2].south = subNodes[0];
        subNodes[2].east = subNodes[3];
        subNodes[3].south = subNodes[1];
        subNodes[3].west = subNodes[2];

        PQ left;
        PQ right;

        if (north.subdivision == subdivision && north.isSubdivided)
        {
            north.GetEdgeQuads(quad, out left, out right);
            subNodes[2].north = left;
            subNodes[3].north = right;
            left.SetNeighbour(quad, subNodes[2]);
            right.SetNeighbour(quad, subNodes[3]);
        }
        else
        {
            subNodes[2].north = north;
            subNodes[3].north = north;
        }

        if (south.subdivision == subdivision && south.isSubdivided)
        {
            south.GetEdgeQuads(quad, out left, out right);
            subNodes[1].south = left;
            subNodes[0].south = right;
            left.SetNeighbour(quad, subNodes[1]);
            right.SetNeighbour(quad, subNodes[0]);
        }
        else
        {
            subNodes[1].south = south;
            subNodes[0].south = south;
        }

        if (east.subdivision == subdivision && east.isSubdivided)
        {
            east.GetEdgeQuads(quad, out left, out right);
            subNodes[3].east = left;
            subNodes[1].east = right;
            left.SetNeighbour(quad, subNodes[3]);
            right.SetNeighbour(quad, subNodes[1]);
        }
        else
        {
            subNodes[3].east = east;
            subNodes[1].east = east;
        }

        if (west.subdivision == subdivision && west.isSubdivided)
        {
            west.GetEdgeQuads(quad, out left, out right);
            subNodes[0].west = left;
            subNodes[2].west = right;
            left.SetNeighbour(quad, subNodes[0]);
            right.SetNeighbour(quad, subNodes[2]);
        }
        else
        {
            subNodes[0].west = west;
            subNodes[2].west = west;
        }

        if (sphereRoot.reqUVQuad)
        {
            Vector2 uvDel;
            Vector2 uvMidPoint;
            Vector2 uvMidS;
            Vector2 uvMidW;
            var uvSW = quad.uvSW;

            uvDel = quad.uvDelta * 0.5f;
            uvMidPoint.x = uvSW.x + uvDel.x;
            uvMidPoint.y = uvSW.y + uvDel.y;
            uvMidS.x = uvMidPoint.x;
            uvMidS.y = uvSW.y;
            uvMidW.x = uvSW.x;
            uvMidW.y = uvMidPoint.y;
            subNodes[0].uvSW = uvMidPoint;
            subNodes[0].uvDelta = uvDel;
            subNodes[1].uvSW = uvMidW;
            subNodes[1].uvDelta = uvDel;
            subNodes[2].uvSW = uvMidS;
            subNodes[2].uvDelta = uvDel;
            subNodes[3].uvSW = uvSW;
            subNodes[3].uvDelta = uvDel;
        }

        quad.isSubdivided = true;
        quad.SetInvisible();

        var tasks = new FixedArray4<ValueTask>();
        for (int i = 0; i < 4; ++i)
        {
            if (
                subNodes[i].north == null
                || subNodes[i].south == null
                || subNodes[i].east == null
                || subNodes[i].west == null
            )
            {
                Debug.Log("Subdivide: " + base.gameObject.name + " " + i);
                Debug.Break();
            }

            if (!subNodes[i].isCached)
                subNodes[i].SetupQuad(quad, (PQ.QuadChild)i);

            if (pqs.quadAllowBuild)
                tasks[i] = UpdateVisibility(subNodes[i]);
        }

        if (pqs.quadAllowBuild)
            await ValueTask.WhenAll(tasks);

        north.QueueForNormalUpdate();
        south.QueueForNormalUpdate();
        east.QueueForNormalUpdate();
        west.QueueForNormalUpdate();
        PQ rightmostCornerPQ = await GetRightmostCornerPQ(quad, north);
        if (rightmostCornerPQ != null)
            rightmostCornerPQ.QueueForCornerNormalUpdate();
        rightmostCornerPQ = await GetRightmostCornerPQ(quad, west);
        if (rightmostCornerPQ != null)
            rightmostCornerPQ.QueueForCornerNormalUpdate();
        rightmostCornerPQ = await GetRightmostCornerPQ(quad, south);
        if (rightmostCornerPQ != null)
            rightmostCornerPQ.QueueForCornerNormalUpdate();
        rightmostCornerPQ = await GetRightmostCornerPQ(quad, east);
        if (rightmostCornerPQ != null)
            rightmostCornerPQ.QueueForCornerNormalUpdate();

        return true;
    }

    async ValueTask UpdateVisibility(PQ quad)
    {
        if (!quad.isSubdivided && quad.gcd1 < pqs.visibleRadius)
        {
            await SetVisible(quad);
            var newEdgeState = quad.GetEdgeState();
            if (newEdgeState != quad.edgeState)
            {
                quad.mesh.triangles = PQS.cacheIndices[(int)newEdgeState];
                quad.edgeState = newEdgeState;
                quad.QueueForNormalUpdate();
            }
        }
        else
        {
            quad.SetInvisible();
        }
    }

    async ValueTask<PQ> GetRightmostCornerPQ(PQ self, PQ nextQuad)
    {
        PQ quad = self;
        if (nextQuad.subdivision < self.subdivision)
            quad = quad.parent;

        var edge = nextQuad.GetEdge(quad);
        if (edge == PQS.QuadEdge.Null)
        {
            if (
                !quad.isPendingCollapse
                && !quad.parent.isPendingCollapse
                && !nextQuad.isPendingCollapse
                && !nextQuad.parent.isPendingCollapse
            )
                Debug.Log(
                    $"[PQ] Edge in GetRightmostCornerPQ is null! Caller: {quad} nextQuad: {nextQuad}"
                );

            return null;
        }

        var rotatedEdge = PQS.GetEdgeRotatedCounterclockwise(edge);
        quad = nextQuad.GetSidePQ(rotatedEdge);
        if (!quad.isBuilt)
            await BuildAsync(quad);
        if (quad.isSubdivided)
            quad.GetEdgeQuads(nextQuad, out _, out quad);
        if (!quad.isBuilt)
            await BuildAsync(quad);
        return quad;
    }
    #endregion

    #region BuildNormals
    delegate void BuildNormalsDelegate(
        in NativeArray<Vector3> verts,
        in NativeArray<int> indices,
        in NativeArray<Vector3> _vertNormals,
        int cacheTriCount
    );

    static BuildNormalsDelegate BuildNormalsFp = null;

    public static unsafe void BuildNormals(PQ quad)
    {
        BuildNormalsFp ??= BurstCompiler
            .CompileFunctionPointer<BuildNormalsDelegate>(BuildNormalsBurst)
            .Invoke;

        fixed (Vector3* verts = quad.verts)
        fixed (int* indices = PQS.cacheIndices[0])
        fixed (Vector3* vertNormals = quad.vertNormals)
        {
            BuildNormalsFp(
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector3>(
                    verts,
                    quad.verts.Length,
                    Allocator.Invalid
                ),
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(
                    indices,
                    PQS.cacheIndices[0].Length,
                    Allocator.Invalid
                ),
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector3>(
                    vertNormals,
                    quad.vertNormals.Length,
                    Allocator.Invalid
                ),
                PQS.cacheTriCount
            );
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    static void BuildNormalsBurst(
        in NativeArray<Vector3> verts,
        in NativeArray<int> indices,
        in NativeArray<Vector3> _vertNormals,
        int cacheTriCount
    )
    {
        var vertNormals = _vertNormals;
        var triNormals = new NativeArray<Vector3>(cacheTriCount, Allocator.Temp);

        vertNormals.Clear();

        for (int i = 0; i < cacheTriCount; i++)
        {
            var ab = verts[indices[i * 3 + 1]] - verts[indices[i * 3]];
            var ac = verts[indices[i * 3 + 2]] - verts[indices[i * 3]];
            var normal = Vector3.Cross(ab, ac);

            triNormals[i] = normal.normalized;
        }

        for (int i = 0; i < cacheTriCount; ++i)
        {
            vertNormals[indices[i * 3 + 0]] += triNormals[i];
            vertNormals[indices[i * 3 + 1]] += triNormals[i];
            vertNormals[indices[i * 3 + 2]] += triNormals[i];
        }

        for (int i = 0; i < vertNormals.Length; ++i)
            vertNormals[i] = vertNormals[i].normalized;
    }
    #endregion

    #region Jobs
    #region InitBuildData
    [BurstCompile]
    struct InitBuildDataJob : IJob
    {
        public Matrix4x4 quadMatrix;
        public BurstQuadBuildData data;
        public bool reqVertexMapCoords;
        public int cacheSideVertCount;
        public float cacheMeshSize;

        public void Execute()
        {
            data.Clear();

            if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
            {
                Debug.LogError("[BurstPQS] CacheVerts length was not equal to data vertex count");
                return;
            }

            float spacing = cacheMeshSize / (cacheSideVertCount - 1);
            float halfSize = cacheMeshSize * 0.5f;
            for (int i = 0, y = 0; y < cacheSideVertCount; ++y)
            {
                for (int x = 0; x < cacheSideVertCount; ++x, ++i)
                {
                    var vert = new Vector3(halfSize - x * spacing, 0f, halfSize - y * spacing);
                    var globalV = quadMatrix.MultiplyPoint3x4(vert);

                    data.globalV[i] = globalV;
                    data.directionFromCenter[i] = globalV.normalized;
                }
            }

            // Set other fields individually so that they can be vectorized
            data.vertHeight.Fill(data.sphere.radius);
            data.allowScatter.Fill(true);

            if (!reqVertexMapCoords)
                return;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var latitude = Math.Asin(MathUtil.Clamp(data.directionFromCenter[i].y, -1.0, 1.0));
                var directionXZ = new Vector3d(
                    data.directionFromCenter[i].x,
                    0.0,
                    data.directionFromCenter[i].z
                );

                double longitude;
                if (directionXZ.sqrMagnitude == 0.0)
                    longitude = 0.0;
                else if (directionXZ.z < 0.0)
                    longitude = Math.PI - Math.Asin(directionXZ.x / directionXZ.magnitude);
                else
                    longitude = Math.Asin(directionXZ.x / directionXZ.magnitude);

                data.latitude[i] = latitude;
                data.longitude[i] = longitude;
                var sy = latitude / Math.PI + 0.5;
                var sx = longitude / Math.PI * 0.5;
                data.u[i] = sx;
                data.v[i] = sy;
            }
        }
    }
    #endregion

    #region BuildVertices
    [BurstCompile]
    struct BuildVerticesJob : IJob
    {
        public BurstQuadBuildData data;
        public CacheData cache;

        [WriteOnly]
        public NativeArray<double> minmax;

        public Matrix4x4 pqsTransform;
        public Matrix4x4 inverseQuadTransform;

        public bool surfaceRelativeQuads;
        public bool reqCustomNormals;
        public bool reqSphereUV;
        public bool reqUVQuad;
        public bool reqUV2;
        public bool reqUV3;
        public bool reqUV4;

        public Vector2 uvSW;
        public Vector2 uvDelta;
        public int cacheSideVertCount;

        public double radius;
        public double meshVertMax;
        public double meshVertMin;

        public void Execute()
        {
            if (data.VertexCount != cache.VertexCount)
            {
                Debug.LogError("[BurstPQS] Data and cache vertex counts were different");
                return;
            }

            if (cacheSideVertCount * cacheSideVertCount != data.VertexCount)
            {
                Debug.LogError("[BurstPQS] side vertex count doesn't match total vertex count");
                return;
            }

            if (surfaceRelativeQuads)
                BuildVertexSurfaceRelative();
            else
                BuildVertexHeight();

            if (!reqCustomNormals)
                BuildNormals();

            // skip reqColorChannel here because data.colors is already in the
            // format that we want.

            if (reqUVQuad)
                BuildVertexQuadUV();
            else if (reqSphereUV)
                BuildVertexSphereUV();

            if (reqUV2)
                BuildUVs(cache.cacheUV2s, data.u2, data.v2);
            if (reqUV3)
                BuildUVs(cache.cacheUV3s, data.u3, data.v3);
            if (reqUV4)
                BuildUVs(cache.cacheUV4s, data.u4, data.v4);

            var vertMax = double.MinValue;
            var vertMin = double.MaxValue;

            foreach (var vheight in data.vertHeight)
            {
                var height = vheight - radius;
                height = MathUtil.Clamp(height, meshVertMin, meshVertMax);

                vertMax = Math.Max(vertMax, height);
                vertMin = Math.Min(vertMin, height);
            }

            minmax[0] = vertMin;
            minmax[1] = vertMax;
        }

        readonly void BuildVertexHeight()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var vert = data.directionFromCenter[i] * data.vertHeight[i];
                cache.verts[i] = vert;
                cache.quadVerts[i] = vert;
            }
        }

        readonly void BuildVertexSurfaceRelative()
        {
            float4x4 pqsTransform = BurstUtil.ConvertMatrix(this.pqsTransform);
            float4x4 inverseQuadTransform = BurstUtil.ConvertMatrix(this.inverseQuadTransform);

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var vert = data.directionFromCenter[i] * data.vertHeight[i];
                var prel = math.mul(
                    pqsTransform,
                    new float4(BurstUtil.ConvertVector((Vector3)vert), 1f)
                );
                var srel = math.mul(inverseQuadTransform, new float4(prel.xyz, 1f));

                cache.verts[i] = vert;
                cache.quadVerts[i] = BurstUtil.ConvertVector(srel.xyz);
            }
        }

        readonly void BuildNormals()
        {
            for (int i = 0; i < data.VertexCount; ++i)
                cache.normals[i] = data.directionFromCenter[i];
        }

        readonly void BuildVertexQuadUV()
        {
            var spacing = 1f / cacheSideVertCount;

            for (int i = 0, y = 0; y < cacheSideVertCount; ++y)
            {
                for (int x = 0; x < cacheSideVertCount; ++x, ++i)
                {
                    var cacheUV = new Vector2(x * spacing, y * spacing);
                    cache.uvs[i] = uvSW + cacheUV * uvDelta;
                }
            }
        }

        readonly void BuildVertexSphereUV()
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                cache.uvs[i] = new(
                    (float)(data.latitude[i] / Math.PI + 0.5),
                    (float)(data.longitude[i] / Math.PI * 0.5)
                );
            }
        }

        readonly void BuildUVs(MemorySpan<Vector2> uvs, MemorySpan<double> u, MemorySpan<double> v)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                uvs[i] = new((float)u[i], (float)v[i]);
        }
    }
    #endregion
    #endregion

#pragma warning disable IDE1006 // Naming Styles
    readonly unsafe struct CacheData : IDisposable
    {
        readonly int _vertexCount;

        [NoAlias]
        readonly void* _data;

        public readonly int VertexCount => _vertexCount;

        #region Offsets
        const int Vector3dSize = 3 * sizeof(double);
        const int Vector3Size = 3 * sizeof(float);
        const int Vector2Size = 2 * sizeof(float);

        const int EndOff_Verts = Vector3dSize;
        const int EndOff_QuadVerts = Vector3Size + EndOff_Verts;
        const int EndOff_Normals = Vector3Size + EndOff_QuadVerts;
        const int EndOff_UVs = Vector2Size + EndOff_Normals;
        const int EndOff_CacheUV2s = Vector2Size + EndOff_UVs;
        const int EndOff_CacheUV3s = Vector2Size + EndOff_CacheUV2s;
        const int EndOff_CacheUV4s = Vector2Size + EndOff_CacheUV3s;

        const int ElemSize = EndOff_CacheUV4s;
        #endregion

        public static int GetTotalAllocationSize(int vertexCount) => vertexCount * EndOff_CacheUV4s;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly MemorySpan<T> GetOffsetSlice<T>(int endOff)
            where T : unmanaged
        {
            return new((T*)((byte*)_data + (endOff - sizeof(T)) * VertexCount), VertexCount);
        }

        public MemorySpan<Vector3d> verts => GetOffsetSlice<Vector3d>(EndOff_Verts);
        public MemorySpan<Vector3> quadVerts => GetOffsetSlice<Vector3>(EndOff_QuadVerts);
        public MemorySpan<Vector3> normals => GetOffsetSlice<Vector3>(EndOff_Normals);
        public MemorySpan<Vector2> uvs => GetOffsetSlice<Vector2>(EndOff_UVs);
        public MemorySpan<Vector2> cacheUV2s => GetOffsetSlice<Vector2>(EndOff_CacheUV2s);
        public MemorySpan<Vector2> cacheUV3s => GetOffsetSlice<Vector2>(EndOff_CacheUV3s);
        public MemorySpan<Vector2> cacheUV4s => GetOffsetSlice<Vector2>(EndOff_CacheUV4s);

        public CacheData(int vertexCount)
        {
            if (vertexCount < 0)
                throw new ArgumentOutOfRangeException(nameof(vertexCount));

            _vertexCount = vertexCount;
            _data = UnsafeUtility.Malloc(ElemSize * vertexCount, sizeof(Color), Allocator.TempJob);
        }

        public void Dispose()
        {
            UnsafeUtility.Free(_data, Allocator.TempJob);
        }
    }
#pragma warning restore IDE1006 // Naming Styles

    struct StateDisposer(List<IBatchPQSModState> states) : IDisposable
    {
        public void Dispose()
        {
            if (states is null)
                return;

            foreach (var state in states)
            {
                if (state is BatchPQSModState disposable)
                    disposable.Dispose();
            }

            states = null;
        }
    }

    #region Method Injections
    internal void PostSetupMods()
    {
        List<BatchPQSMod> batchMods = new(pqs.mods.Length);
        foreach (var mod in pqs.mods)
        {
            try
            {
                var batchMod = BatchPQSMod.Create(mod);
                if (batchMod is not null)
                    batchMods.Add(batchMod);
            }
            catch (UnsupportedPQSModException)
            {
                Debug.LogWarning(
                    $"[BurstPQS] PQSMod {mod.GetType().Name} is not supported by BatchPQS"
                );
                fallback = true;
            }
        }

        if (fallback)
            Debug.LogWarning(
                $"[BurstPQS] BatchPQS not supported for surface {pqs.name}. Falling back to regular PQS"
            );
        else
            Debug.Log($"[BurstPQS] BatchPQS enabled for surface {pqs.name}");

        this.mods = [.. batchMods];

        if (!fallback)
        {
            foreach (var mod in this.mods)
            {
                try
                {
                    mod.OnSetup();
                }
                catch (UnsupportedPQSModException e)
                {
                    Debug.LogWarning(
                        $"[BatchPQS] PQSMod {mod.GetType().Name} is not supported by BatchPQS: {e.Message}"
                    );
                    fallback = true;
                }
            }
        }
    }
    #endregion
}
