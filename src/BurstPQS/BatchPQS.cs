using System;
using System.Collections.Generic;
using System.Text;
using BurstPQS.Jobs;
using BurstPQS.Patches;
using BurstPQS.Util;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;

namespace BurstPQS;

public class BatchPQS : MonoBehaviour
{
    internal static bool ForceFallback = false;
    static readonly ProfilerMarker BuildQuadMarker = new("BatchPQS.BuildQuad");

    private PQS pqs;
    private CelestialBody body;
    private BatchPQSMod[] mods;
    private PQSMod_QuadMeshColliders colliderMod;

    // Are there unsupported mods and do we need to fall back to the stock
    // implementation?
    private bool _fallback = false;
    internal bool Fallback
    {
        get => _fallback || ForceFallback;
        set => _fallback = value;
    }
    internal string FallbackMessage { get; private set; }

    private readonly Dictionary<PQ, PendingBuild> pending = [];
    private readonly Queue<PQ> buildQueue = [];
    private NativeList<MeshDataStruct> disposeList;

    // Patches look this up very often, and usually for the same PQS repeatedly,
    // so remember the last one.
    static int cachedPQSId;
    static BatchPQS cachedBatchPQS;

    internal static BatchPQS Get(PQS pqs)
    {
        if (pqs.IsNullOrDestroyed())
            return null;

        int id = pqs.GetInstanceID();
        if (id == cachedPQSId && cachedBatchPQS.IsNotNullOrDestroyed())
            return cachedBatchPQS;

        var batchPQS = pqs.GetComponent<BatchPQS>();
        if (batchPQS.IsNullOrDestroyed())
            return null;

        cachedPQSId = id;
        cachedBatchPQS = batchPQS;
        return batchPQS;
    }

    void Awake()
    {
        pqs = GetComponent<PQS>();
        body = GetComponentInParent<CelestialBody>();

        storageTransforms = new TransformAccessArray(64);
        storagePlanetPositions = new NativeList<double3>(64, Allocator.Persistent);
        snapshots = new NativeList<QuadSnapshot>(2048, Allocator.Persistent);
    }

    void OnDestroy()
    {
        storageHandle.Complete();
        storageTransforms.Dispose();
        storagePlanetPositions.Dispose();
        snapshots.Dispose();

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

    public bool BuildQuad(PQ quad)
    {
        using var scope = BuildQuadMarker.Auto();

        if (pending.TryGetValue(quad, out var build))
            pending.Remove(quad);
        else if (Fallback)
            return PQS_RevPatch.BuildQuad(pqs, quad);
        else
        {
            build = new(this, quad);

            if (!build.StartBuild())
                return false;
        }

        using (build)
            build.Complete();
        return true;
    }

    #region Precise Frame
    // The PQS transform's world position and rotation are floats rounded at
    // planet scale. The body keeps both in doubles, so start from the body and
    // walk down to the PQS through the local transforms in between.
    internal double4x4 GetPreciseLocalToWorld()
    {
        var transform = pqs.transform;

        if (
            body != null
            && TryGetBodyFrame(body, out var bodyToWorld)
            && TryGetRelativeTransform(transform, body.bodyTransform, out var pqsToBody)
        )
            return math.mul(bodyToWorld, pqsToBody);

        return new double4x4(BurstUtil.ConvertMatrix(transform.localToWorldMatrix));
    }

    static bool TryGetBodyFrame(CelestialBody body, out double4x4 localToWorld)
    {
        localToWorld = default;

        var bodyTransform = body.bodyTransform;
        if (bodyTransform == null)
            return false;

        // The body's double-precision position and rotation should match its
        // transform up to float rounding. If they don't, then they aren't
        // being kept up to date and we can't use them.
        var position = body.position;
        var tolerance = Math.Max(1.0, position.magnitude * 1e-6);
        if (((Vector3d)bodyTransform.position - position).magnitude > tolerance)
            return false;

        var rotation = body.rotation;
        var trot = bodyTransform.rotation;
        var dot =
            rotation.x * trot.x + rotation.y * trot.y + rotation.z * trot.z + rotation.w * trot.w;
        if (Math.Abs(dot) < 1.0 - 1e-6)
            return false;

        // lossyScale is derived from the float world matrix and comes out a
        // few ULPs off of 1, which is metres at planet scale. KSP doesn't
        // scale bodies when placing vessels either, so leave it out.
        localToWorld = Affine(
            BurstUtil.RotationMatrix(rotation),
            BurstUtil.ConvertVector(position)
        );
        return true;
    }

    static bool TryGetRelativeTransform(Transform from, Transform to, out double4x4 fromToTo)
    {
        fromToTo = double4x4.identity;

        for (var t = from; t != to; t = t.parent)
        {
            if (t == null)
                return false;

            var local = Affine(
                math.mul(BurstUtil.RotationMatrix(t.localRotation), ScaleMatrix(t.localScale)),
                BurstUtil.ConvertVector(t.localPosition)
            );
            fromToTo = math.mul(local, fromToTo);
        }

        return true;
    }

    static double4x4 Affine(double3x3 linear, double3 translation) =>
        new(
            new double4(linear.c0, 0.0),
            new double4(linear.c1, 0.0),
            new double4(linear.c2, 0.0),
            new double4(translation, 1.0)
        );

    static double3x3 ScaleMatrix(Vector3 scale) =>
        new(scale.x, 0.0, 0.0, 0.0, scale.y, 0.0, 0.0, 0.0, scale.z);
    #endregion

    #region Local Space Storage
    // Stock only moves max level quads into LocalSpacePQStorage. Everything
    // else is parented to the sphere, so its world position comes from a float
    // offset the size of the planet radius. Quads in storage don't rotate with
    // the body though, so we can only put them there while the body is fixed in
    // world space. When that changes we move every non-root quad in or out.
    //
    // We track storage quads ourselves instead of using LocalSpacePQList so
    // that floating origin updates can be done by a job. Stock's list stays
    // empty for spheres we manage.
    private bool useLocalStorage;
    private readonly List<PQ> storageQuads = [];
    private readonly Dictionary<PQ, int> storageIndices = [];
    private TransformAccessArray storageTransforms;
    private NativeList<double3> storagePlanetPositions;
    private JobHandle storageHandle;

    bool ManagesQuadPlacement => !Fallback && pqs.surfaceRelativeQuads && body != null;

    internal void UpdateLocalStorage()
    {
        if (!ManagesQuadPlacement)
            return;

        bool use = body.inverseRotation || !body.rotates;
        if (use == useLocalStorage)
            return;

        useLocalStorage = use;
        if (pqs.quads == null)
            return;

        var planetToWorld = GetPreciseLocalToWorld();
        foreach (var root in pqs.quads)
        {
            if (root != null)
                PlaceSubQuads(root, planetToWorld);
        }
    }

    void PlaceSubQuads(PQ quad, in double4x4 planetToWorld)
    {
        foreach (var child in quad.subNodes)
        {
            if (child == null)
                continue;

            PlaceQuad(child, planetToWorld);
            PlaceSubQuads(child, planetToWorld);
        }
    }

    internal void PlaceQuad(PQ quad)
    {
        if (!ManagesQuadPlacement || quad.quadRoot == null)
            return;

        PlaceQuad(quad, GetPreciseLocalToWorld());
    }

    void PlaceQuad(PQ quad, in double4x4 planetToWorld)
    {
        storageHandle.Complete();

        // Stock may have already put max level quads into its own list.
        if (quad.subdivision == pqs.maxLevel)
            pqs.RemovePQFromLocalSpaceStorage(quad);

        var transform = quad.quadTransform;
        if (transform.parent != pqs.transform)
            transform.parent = pqs.transform;
        transform.localPosition = quad.positionPlanet;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        if (!useLocalStorage)
        {
            RemoveFromStorage(quad);
            return;
        }

        // Use the float local position so the mesh origin is the same whether
        // or not the quad is in storage.
        var localPosition = transform.localPosition;
        var planetPosition = new double3(localPosition.x, localPosition.y, localPosition.z);
        var position = math.mul(planetToWorld, new double4(planetPosition, 1.0)).xyz;

        transform.parent = pqs.LocalSpacePQStorage.transform;
        transform.position = new Vector3((float)position.x, (float)position.y, (float)position.z);

        if (storageIndices.TryGetValue(quad, out var index))
        {
            storagePlanetPositions[index] = planetPosition;
        }
        else
        {
            storageIndices.Add(quad, storageQuads.Count);
            storageQuads.Add(quad);
            storageTransforms.Add(transform);
            storagePlanetPositions.Add(planetPosition);
        }
    }

    void RemoveFromStorage(PQ quad)
    {
        if (!storageIndices.TryGetValue(quad, out var index))
            return;

        storageHandle.Complete();
        storageIndices.Remove(quad);

        int last = storageQuads.Count - 1;
        if (index != last)
        {
            var moved = storageQuads[last];
            storageQuads[index] = moved;
            storageIndices[moved] = index;
        }

        storageQuads.RemoveAt(last);
        storageTransforms.RemoveAtSwapBack(index);
        storagePlanetPositions.RemoveAtSwapBack(index);
    }

    internal void UpdateStoragePositions()
    {
        if (storageQuads.Count == 0)
            return;

        storageHandle = new UpdateStoragePositionsJob
        {
            planetToWorld = GetPreciseLocalToWorld(),
            planetPositions = storagePlanetPositions.AsArray(),
        }.Schedule(storageTransforms, storageHandle);
        JobHandle.ScheduleBatchedJobs();
    }

    // Meshes need to be built relative to a planet frame origin. The quad's
    // world position is rounded differently after every floating origin shift,
    // so building against it leaves the terrain offset once the origin moves.
    internal bool TryGetPlanetToQuad(PQ quad, out double4x4 planetToQuad)
    {
        storageHandle.Complete();

        if (storageIndices.TryGetValue(quad, out var index))
        {
            planetToQuad = Affine(double3x3.identity, -storagePlanetPositions[index]);
            return true;
        }

        if (TryGetRelativeTransform(quad.quadTransform, pqs.transform, out var quadToPlanet))
        {
            planetToQuad = math.inverse(quadToPlanet);
            return true;
        }

        planetToQuad = default;
        return false;
    }
    #endregion

    #region UpdateQuads
    static readonly ProfilerMarker UpdateQuadsMarker = new("UpdateQuads");
    static readonly ProfilerMarker UpdateQuadsInitMarker = new("UpdateQuadsInit");
    static readonly ProfilerMarker UpdateTargetRelativityMarker = new("UpdateTargetRelativity");
    static readonly ProfilerMarker UpdateSubdivisionMarker = new("UpdateSubdivision");
    static readonly ProfilerMarker CompleteQueuedBuildsMarker = new("CompleteQueuedBuilds");
    static readonly ProfilerMarker UpdateEdgesMarker = new("UpdateEdges");
    static readonly ProfilerMarker UpdateMeshRenderersMarker = new("UpdateMeshRenderers");

    public void UpdateQuadsInit()
    {
        using var scope = UpdateQuadsInitMarker.Auto();

        pqs.CreateQuads();
        pqs.isThinking = true;
        pqs.quadAllowBuild = false;

        // Match stock iteration: reverse order (5→0) and require multiple
        // consecutive stable iterations to ensure the tree fully converges.
        int stable = 0;
        while (stable < 10)
        {
            int prev = pqs.quadCount;
            for (int i = pqs.quads.Length - 1; i >= 0; i--)
                pqs.quads[i].UpdateSubdivisionInit();
            stable = prev == pqs.quadCount ? stable + 1 : 0;
        }

        pqs.quadAllowBuild = true;

        using (var subdivisionUpdate = new SubdivisionUpdate(this, activeQuads))
        {
            subdivisionUpdate.ScheduleJobs();
            subdivisionUpdate.Complete();
        }

        JobHandle.ScheduleBatchedJobs();
        CompleteQueuedBuilds();
        JobHandle.ScheduleBatchedJobs();

        // Forcibly disable all mesh renderers for subdivided quads.
        //
        // This doesn't get properly set-up during UpdateSubdivisionInit.
        // In stock, KSP sets enabled every single frame so it gets fixed during
        // the next UpdateQuads call. We don't do that, so this is a one-time
        // fix to make sure that everything is in the right state.
        RefreshQuadSnapshots();
        foreach (var q in activeQuads)
        {
            if (q.IsNotNullOrDestroyed() && q.isSubdivided)
                q.meshRenderer.enabled = false;
        }

        pqs.isThinking = false;
    }

    public void UpdateQuads()
    {
        if (pqs.quads == null)
            return;

        using var scope = UpdateQuadsMarker.Auto();

        pqs.isThinking = true;

        SortQuadsByDistance(pqs.quads, pqs.relativeTargetPosition);

        using var subdivisionUpdate = new SubdivisionUpdate(this, activeQuads);
        subdivisionUpdate.ScheduleJobs();
        subdivisionUpdate.UpdateMeshRenderers();
        subdivisionUpdate.Complete();

        JobHandle.ScheduleBatchedJobs();

        CompleteQueuedBuilds();
        JobHandle.ScheduleBatchedJobs();

        if (pqs.reqCustomNormals)
        {
            QueueEdgeBuilds();
            using (UpdateEdgesMarker.Auto())
                pqs.UpdateEdges();
        }

        pqs.isThinking = false;
    }

    readonly struct BatchDisposeScope : IDisposable
    {
        readonly BatchPQS batchPQS;

        public BatchDisposeScope(BatchPQS batchPQS, int capacity)
        {
            this.batchPQS = batchPQS;
            batchPQS.disposeList = new NativeList<MeshDataStruct>(capacity, Allocator.TempJob);
        }

        public void Dispose()
        {
            var list = batchPQS.disposeList;
            batchPQS.disposeList = default;
            if (list.Length == 0)
                list.Dispose();
            else
                new MeshDataStruct.BatchDisposeJob(list).Schedule();
        }
    }

    void CompleteQueuedBuilds()
    {
        using var scope = CompleteQueuedBuildsMarker.Auto();
        using var disposeScope = new BatchDisposeScope(this, buildQueue.Count);

        while (buildQueue.TryDequeue(out var quad))
        {
            if (!pending.TryGetValue(quad, out var build))
                continue;

            pending.Remove(quad);

            try
            {
                // PendingBuild.Complete() applies edge stitching. Stock code fixes
                // stitching every frame via the recursive UpdateSubdivision walk, but
                // we use selective UpdateVisibility, so the build has to do it.
                build.Complete();
                quad.isBuilt = true;

                quad.QueueForNormalUpdate();
            }
            finally
            {
                build.Dispose();
            }
        }
    }

    static bool HasNeighbours(PQ quad) =>
        quad.north != null && quad.south != null && quad.east != null && quad.west != null;

    // Setting mesh.triangles recalculates the submesh bounds by walking every
    // index. Quad vertices never change when only the indices do, so reuse
    // the bounds we already have.
    static void SetIndices(
        Mesh mesh,
        int[] indices,
        Bounds bounds,
        int vertexCount,
        MeshUpdateFlags flags
    )
    {
        mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
        mesh.SetIndexBufferData(indices, 0, 0, indices.Length, flags);
        if (mesh.subMeshCount != 1)
            mesh.subMeshCount = 1;
        mesh.SetSubMesh(
            0,
            new SubMeshDescriptor(0, indices.Length)
            {
                bounds = bounds,
                firstVertex = 0,
                vertexCount = vertexCount,
            },
            flags | MeshUpdateFlags.DontRecalculateBounds
        );
    }

    // Replaces mesh.triangles in PQ.UpdateVisibility.
    internal static void SetQuadTriangles(Mesh mesh, int[] indices)
    {
        int vertexCount = mesh.vertexCount;
        if (vertexCount != PQS.cacheVertCount)
        {
            mesh.triangles = indices;
            return;
        }

        SetIndices(
            mesh,
            indices,
            mesh.bounds,
            vertexCount,
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds
        );
    }

    const int NormalStream = 1;
    static readonly VertexAttributeDescriptor[] AttributeScratch = new VertexAttributeDescriptor[16];

    // Replaces mesh.normals in PQS.UpdateEdgeNormals. Meshes built by
    // PendingBuild keep normals in their own stream, so they can be uploaded
    // directly without the setter's validation and notifications.
    internal static void SetQuadNormals(Mesh mesh, Vector3[] normals)
    {
        if (mesh.vertexCount != normals.Length || !HasSeparateNormalStream(mesh))
        {
            mesh.normals = normals;
            return;
        }

        mesh.SetVertexBufferData(
            normals,
            0,
            0,
            normals.Length,
            NormalStream,
            MeshUpdateFlags.DontValidateIndices
                | MeshUpdateFlags.DontResetBoneBounds
                | MeshUpdateFlags.DontNotifyMeshUsers
                | MeshUpdateFlags.DontRecalculateBounds
        );
    }

    static bool HasSeparateNormalStream(Mesh mesh)
    {
        int count = mesh.GetVertexAttributes(AttributeScratch);
        bool found = false;

        for (int i = 0; i < count; ++i)
        {
            var attr = AttributeScratch[i];
            if (attr.stream != NormalStream)
                continue;

            if (
                attr.attribute != VertexAttribute.Normal
                || attr.format != VertexAttributeFormat.Float32
                || attr.dimension != 3
            )
                return false;

            found = true;
        }

        return found;
    }

    static readonly ProfilerMarker QueueEdgeBuildsMarker = new("QueueEdgeBuilds");

    void QueueEdgeBuilds()
    {
        if (Fallback)
            return;

        using var scope = QueueEdgeBuildsMarker.Auto();

        var list = pqs.normalUpdateList;
        for (int i = 0; i < list.Count; i++)
        {
            var q = list[i];
            if (q == null || q.isSubdivided || !q.isVisible)
                continue;
            if (!q.isActive || !q.isBuilt)
                continue;
            if (q.parent.IsNullOrDestroyed() || !q.parent.isSubdivided)
                continue;

            QueueNeighborBuild(q, q.north);
            QueueNeighborBuild(q, q.south);
            QueueNeighborBuild(q, q.east);
            QueueNeighborBuild(q, q.west);

            QueueCornerBuild(q, q.north);
            QueueCornerBuild(q, q.west);
            QueueCornerBuild(q, q.south);
            QueueCornerBuild(q, q.east);
        }

        JobHandle.ScheduleBatchedJobs();
    }

    void QueueNeighborBuild(PQ q, PQ neighbor)
    {
        if (neighbor.subdivision == q.subdivision)
        {
            if (neighbor.isSubdivided)
            {
                neighbor.GetEdgeQuads(q, out var left, out var right);
                if (left.IsNotNullOrDestroyed())
                    BuildDeferred(left);
                if (right.IsNotNullOrDestroyed())
                    BuildDeferred(right);
            }
            else
            {
                BuildDeferred(neighbor);
            }
        }
        else if (neighbor.subdivision < q.subdivision)
        {
            BuildDeferred(neighbor);
        }
    }

    void QueueCornerBuild(PQ q, PQ neighbor)
    {
        // GetRightmostCornerPQ internally calls BuildDeferred (transpiled)
        // on intermediate quads. We also BuildDeferred the result to cover
        // the Build() call in GetRightmostCornerNormal.
        var cornerPQ = q.GetRightmostCornerPQ(neighbor);
        if (cornerPQ.IsNotNullOrDestroyed())
            BuildDeferred(cornerPQ);
    }

    #region Subdivision Targets
    // Stock only subdivides around pqs.target, which is the active vessel in
    // flight. Other loaded vessels and the camera need terrain detail too, so
    // they are added as extra targets alongside the stock one.
    JobHandle ScheduleSubdivisionTargets(NativeList<SubdivisionTarget> targets)
    {
        var inputs = new NativeList<SubdivisionTargetInput>(
            FlightGlobals.VesselsLoaded.Count + 1,
            Allocator.TempJob
        );

        // The speed cap must not drop below the level that has colliders, or
        // vessels lose the ground under them while moving. The camera doesn't
        // need colliders so it keeps the normal cap.
        int colliderLevel = GetColliderLevel();

        if (HighLogic.LoadedSceneIsFlight && body != null)
        {
            // The camera follows the active vessel, so use the terrain and
            // speed under it.
            var camera = FlightCamera.fetch;
            var activeVessel = FlightGlobals.ActiveVessel;
            if (camera != null && camera.mainCamera != null)
            {
                inputs.Add(
                    new SubdivisionTargetInput
                    {
                        worldPosition = BurstUtil.ConvertVector(
                            (Vector3d)camera.mainCamera.transform.position
                        ),
                        surfaceRadius = pqs.targetDistance - pqs.targetHeight,
                        surfaceSpeed = activeVessel != null ? activeVessel.srfSpeed : 0.0,
                    }
                );
            }

            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (vessel == null || vessel.mainBody != body || vessel.transform == pqs.target)
                    continue;

                inputs.Add(
                    new SubdivisionTargetInput
                    {
                        worldPosition = BurstUtil.ConvertVector(vessel.GetWorldPos3D()),
                        surfaceRadius = pqs.radius + vessel.terrainAltitude,
                        surfaceSpeed = vessel.srfSpeed,
                        colliderLevel = colliderLevel,
                    }
                );
            }
        }

        return new PrepareSubdivisionTargetsJob
        {
            primary = new SubdivisionTarget
            {
                directionNormalized = BurstUtil.ConvertVector(
                    pqs.relativeTargetPositionNormalized
                ),
                absHeight = Math.Abs(pqs.targetHeight),
                collapseFactor = pqs.collapseThreshold,
                maxLevelAtSpeed = HighLogic.LoadedSceneIsFlight
                    ? Math.Max(pqs.maxLevelAtCurrentTgtSpeed, colliderLevel)
                    : pqs.maxLevelAtCurrentTgtSpeed,
            },
            planetToWorld = inputs.Length != 0 ? GetPreciseLocalToWorld() : double4x4.identity,
            radius = pqs.radius,
            maxDetailDistance = pqs.maxDetailDistance,
            collapseSeaLevelValue = pqs.collapseSeaLevelValue,
            collapseAltitudeValue = pqs.collapseAltitudeValue,
            collapseDelta = pqs.collapseDelta,
            maxQuadLengthsPerFrame = pqs.maxQuadLenghtsPerFrame,
            fixedDeltaTime = Time.fixedDeltaTime,
            minLevel = pqs.minLevel,
            maxLevel = pqs.maxLevel,
            inputs = inputs,
            targets = targets,
        }.Schedule();
    }

    int GetColliderLevel()
    {
        if (colliderMod == null || !colliderMod.modEnabled)
            return 0;

        return pqs.maxLevel - Math.Abs(colliderMod.maxLevelOffset);
    }
    #endregion

    #region SubdivisionUpdate
    // Cache PQ fields in native memory to avoid reading every managed quad each frame.
    // Rebuild the cache when the tree resets; otherwise refresh affected quads.
    private readonly List<PQ> activeQuads = new(2048);
    private readonly Dictionary<PQ, int> activeQuadIndices = new(2048);
    private readonly List<PQ> quadsToRefresh = [];
    private NativeList<QuadSnapshot> snapshots;
    private bool needsQuadRescan = true;
    private int activeQuadWalkIndex = 0;

    private readonly List<PQ> subdivideQuads = [];
    private readonly List<PQ> collapseQuads = [];
    private readonly List<PQ> onUpdateQuads = [];

    internal void InvalidateActiveQuads() => needsQuadRescan = true;

    internal void InvalidateQuadSnapshot(PQ quad)
    {
        if (!needsQuadRescan)
            quadsToRefresh.Add(quad);
    }

    internal void OnQuadCreated(PQ quad)
    {
        if (needsQuadRescan)
            return;

        TrackQuad(quad);
    }

    internal void OnQuadDestroying(PQ quad) => UntrackQuad(quad);

    void TrackQuad(PQ quad)
    {
        if (activeQuadIndices.ContainsKey(quad))
            return;

        activeQuadIndices.Add(quad, activeQuads.Count);
        activeQuads.Add(quad);
        snapshots.Add(default);
        InvalidateQuadSnapshot(quad);
    }

    void UntrackQuad(PQ quad)
    {
        if (!activeQuadIndices.TryGetValue(quad, out var index))
            return;

        activeQuadIndices.Remove(quad);

        int last = activeQuads.Count - 1;
        if (index != last)
        {
            var moved = activeQuads[last];
            activeQuads[index] = moved;
            activeQuadIndices[moved] = index;
        }

        activeQuads.RemoveAt(last);
        snapshots.RemoveAtSwapBack(index);
    }

    void RefreshQuadSnapshots()
    {
        if (needsQuadRescan)
            RebuildActiveQuads();

        foreach (var quad in quadsToRefresh)
        {
            if (activeQuadIndices.TryGetValue(quad, out var index))
                snapshots[index] = CreateSnapshot(quad);
        }
        quadsToRefresh.Clear();
    }

    static QuadSnapshot CreateSnapshot(PQ q) =>
        new()
        {
            positionPlanetRelative = BurstUtil.ConvertVector(q.positionPlanetRelative),
            angularInterval = q.angularinterval,
            subdivideThresholdFactor = q.subdivideThresholdFactor,
            subdivision = q.subdivision,
            isSubdivided = q.isSubdivided,
            isVisible = q.isVisible,
            hasOnUpdate = q.onUpdate != null,
        };

    struct SubdivisionUpdate(BatchPQS batchPQS, List<PQ> activeQuads) : IDisposable
    {
        readonly PQS pqs = batchPQS.pqs;
        readonly BatchPQS batchPQS = batchPQS;
        readonly List<PQ> activeQuads = activeQuads;

        NativeArray<QuadResult> results;
        NativeArray<SubdivisionAction> actions;
        NativeList<int> subdivideIndices;
        NativeList<int> collapseIndices;
        NativeList<int> onUpdateIndices;
        NativeQueue<int> visibilityChangedQueue;
        JobHandle subdivideHandle;
        JobHandle collapseHandle;
        JobHandle scatterHandle;
        JobHandle onUpdateHandle;

        public void ScheduleJobs()
        {
            using var scope = UpdateTargetRelativityMarker.Auto();

            batchPQS.RefreshQuadSnapshots();

            int count = activeQuads.Count;
            if (count == 0)
                return;

            var snapshots = batchPQS.snapshots.AsArray();

            results = new NativeArray<QuadResult>(count, Allocator.TempJob);
            actions = new NativeArray<SubdivisionAction>(count, Allocator.TempJob);
            onUpdateIndices = new NativeList<int>(64, Allocator.TempJob);
            visibilityChangedQueue = new NativeQueue<int>(Allocator.TempJob);

            // Copy threshold arrays into NativeArrays for Burst access
            var subdivThresholds = new NativeArray<double>(
                pqs.subdivisionThresholds,
                Allocator.TempJob
            );
            var targets = new NativeList<SubdivisionTarget>(Allocator.TempJob);
            var targetsHandle = batchPQS.ScheduleSubdivisionTargets(targets);

            var computeHandle = new ComputeSubdivisionJob
            {
                radius = pqs.radius,
                targets = targets.AsDeferredJobArray(),
                subdivisionThresholds = subdivThresholds,
                collapseLevels = pqs.collapseThresholds.Length,
                maxLevel = pqs.maxLevel,
                minLevel = pqs.minLevel,
                visibleRadius = pqs.visibleRadius,
                snapshots = snapshots,
                actions = actions,
                results = results,
                visibilityChangedQueue = visibilityChangedQueue.AsParallelWriter(),
            }.ScheduleBatch(count, 128, targetsHandle);

            var scatterQuadsHandle = new ObjectHandle<List<PQ>>(activeQuads);
            scatterHandle = new ScatterQuadResultsJob
            {
                quads = scatterQuadsHandle,
                results = results,
            }.ScheduleBatch(activeQuads.Count, 32, computeHandle);

            onUpdateHandle = new CollectOnUpdateJob
            {
                snapshots = snapshots,
                onUpdateIndices = onUpdateIndices,
            }.Schedule();

            subdivideIndices = new NativeList<int>(64, Allocator.TempJob);
            collapseIndices = new NativeList<int>(64, Allocator.TempJob);

            subdivideHandle = new CollectActionsJob
            {
                actions = actions,
                snapshots = snapshots,
                results = results,
                target = SubdivisionAction.Subdivide,
                indices = subdivideIndices,
            }.Schedule(computeHandle);
            collapseHandle = new CollectActionsJob
            {
                actions = actions,
                snapshots = snapshots,
                results = results,
                target = SubdivisionAction.Collapse,
                indices = collapseIndices,
            }.Schedule(computeHandle);

            scatterQuadsHandle.Dispose(scatterHandle);
            subdivThresholds.Dispose(computeHandle);
            targets.Dispose(computeHandle);
            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>
        /// Walks <see cref="activeQuads"/> while the subdivide job runs and corrects
        /// each quad's <c>meshRenderer.enabled</c> state. Stock KSP rewrites this every
        /// frame in its recursive UpdateSubdivision walk; we don't, so renderers can drift
        /// out of sync (notably, subdivided parents left visible). Processes at least 5
        /// quads per call, then keeps going until <see cref="subdivideHandle"/> completes
        /// or the walk runs off the end of the list (in which case the index resets).
        /// </summary>
        public void UpdateMeshRenderers()
        {
            using var scope = UpdateMeshRenderersMarker.Auto();

            if (activeQuads.Count == 0)
                return;

            const int MinProcessed = 5;
            int processed = 0;

            while (true)
            {
                if (batchPQS.activeQuadWalkIndex >= activeQuads.Count)
                {
                    batchPQS.activeQuadWalkIndex = 0;
                    return;
                }

                var q = activeQuads[batchPQS.activeQuadWalkIndex++];
                if (q.IsNotNullOrDestroyed() && q.meshRenderer.IsNotNullOrDestroyed())
                {
                    bool shouldBeEnabled = !q.isSubdivided && q.isVisible && !q.isForcedInvisible;
                    if (q.meshRenderer.enabled != shouldBeEnabled)
                        q.meshRenderer.enabled = shouldBeEnabled;
                }

                processed++;
                if (processed >= MinProcessed && subdivideHandle.IsCompleted)
                    return;
            }
        }

        public void Complete()
        {
            using var scope = UpdateSubdivisionMarker.Auto();

            if (!subdivideIndices.IsCreated)
                return;

            // Tree changes can reorder the quad list and snapshots. Finish all
            // readers and resolve their indices before changing the tree.
            JobHandle.CompleteAll(ref subdivideHandle, ref collapseHandle, ref onUpdateHandle);
            scatterHandle.Complete();

            var subdivideQuads = ResolveQuads(subdivideIndices, batchPQS.subdivideQuads);
            var collapseQuads = ResolveQuads(collapseIndices, batchPQS.collapseQuads);
            var onUpdateQuads = ResolveQuads(onUpdateIndices, batchPQS.onUpdateQuads);

            // Cached children can reactivate without a QuadCreated call.
            foreach (var q in subdivideQuads)
            {
                if (!q.IsSafeToSubdivide() || !q.Subdivide())
                    continue;

                batchPQS.InvalidateQuadSnapshot(q);
                foreach (var child in q.subNodes)
                    batchPQS.InvalidateQuadSnapshot(child);
            }

            foreach (var q in collapseQuads)
            {
                if (q.IsSafeToCollapse() && q.Collapse())
                    batchPQS.InvalidateQuadSnapshot(q);
            }

            bool modified = subdivideIndices.Length != 0 || collapseIndices.Length != 0;
            if (modified)
            {
                // Tree structure changed — edge states may have changed for neighbors,
                // so we must call UpdateVisibility on all leaf quads to fix T-junctions.
                for (int i = 0; i < activeQuads.Count; i++)
                {
                    var q = activeQuads[i];
                    if (q.IsNotNullOrDestroyed() && q.isActive && !q.isSubdivided)
                        UpdateVisibility(q, i);
                }
            }
            else
            {
                // No tree changes — only update quads whose visibility actually flipped.
                // Edge stitching is stable since no subdivision levels changed.
                while (visibilityChangedQueue.TryDequeue(out int idx))
                {
                    var q = activeQuads[idx];
                    if (q.IsNotNullOrDestroyed() && q.isActive && !q.isSubdivided)
                        UpdateVisibility(q, idx);
                }
            }

            foreach (var q in onUpdateQuads)
            {
                if (q.IsNotNullOrDestroyed() && q.isActive)
                    q.onUpdate?.Invoke(q);
            }

            subdivideQuads.Clear();
            collapseQuads.Clear();
            onUpdateQuads.Clear();
        }

        readonly void UpdateVisibility(PQ q, int index)
        {
            q.UpdateVisibility();
            if (q.isVisible != batchPQS.snapshots[index].isVisible)
                batchPQS.InvalidateQuadSnapshot(q);
        }

        readonly List<PQ> ResolveQuads(NativeList<int> indices, List<PQ> quads)
        {
            quads.Clear();
            foreach (var index in indices)
                quads.Add(activeQuads[index]);
            return quads;
        }

        public void Dispose()
        {
            JobHandle.CompleteAll(ref subdivideHandle, ref collapseHandle, ref onUpdateHandle);
            scatterHandle.Complete();

            results.Dispose();
            actions.Dispose();
            if (subdivideIndices.IsCreated)
                subdivideIndices.Dispose();
            if (collapseIndices.IsCreated)
                collapseIndices.Dispose();
            if (onUpdateIndices.IsCreated)
                onUpdateIndices.Dispose();
            if (visibilityChangedQueue.IsCreated)
                visibilityChangedQueue.Dispose();
        }
    }
    #endregion

    static void SortQuadsByDistance(PQ[] quads, Vector3d relativeTargetPosition)
    {
        int num = quads.Length;
        for (int i = 1; i < num; i++)
        {
            PQ pQ = quads[i];
            int j = i - 1;
            while (
                j >= 0
                && (relativeTargetPosition - quads[j].positionPlanetRelative).sqrMagnitude
                    > (relativeTargetPosition - pQ.positionPlanetRelative).sqrMagnitude
            )
            {
                quads[j + 1] = quads[j];
                j--;
            }
            quads[j + 1] = pQ;
        }
    }

    static readonly ProfilerMarker RebuildActiveQuadsMarker = new("RebuildActiveQuads");

    void RebuildActiveQuads()
    {
        using var scope = RebuildActiveQuadsMarker.Auto();

        activeQuads.Clear();
        activeQuadIndices.Clear();
        quadsToRefresh.Clear();
        snapshots.Clear();
        needsQuadRescan = false;

        if (pqs.quads == null)
            return;

        foreach (var quad in pqs.quads)
        {
            if (quad.IsNullOrDestroyed() || !quad.isActive)
                continue;

            TrackQuad(quad);
        }

        for (int i = 0; i < activeQuads.Count; ++i)
        {
            var quad = activeQuads[i];

            if (!quad.isSubdivided)
                continue;

            foreach (var subnode in quad.subNodes)
            {
                if (subnode.IsNullOrDestroyed() || !subnode.isActive)
                    continue;

                TrackQuad(subnode);
            }
        }
    }
    #endregion

    internal void BuildDeferred(PQ quad)
    {
        if (quad.isBuilt || !quad.isActive || !pqs.quadAllowBuild || quad.isCached)
            return;

        if (Fallback)
        {
            quad.Build();
            return;
        }

        if (quad.isSubdivided)
        {
            foreach (var subnode in quad.subNodes)
                BuildDeferred(subnode);
        }
        else
        {
            if (pending.ContainsKey(quad))
                return;

            var build = new PendingBuild(this, quad);
            if (!build.StartBuild())
                return;

            buildQueue.Enqueue(quad);
            pending.Add(quad, build);
            JobHandle.ScheduleBatchedJobs();
        }
    }

    static readonly ProfilerMarker OnQuadSubdividedMarker = new("BatchPQS.OnQuadSubdivided");

    public void OnQuadSubdivided(PQ quad)
    {
        using var scope = OnQuadSubdividedMarker.Auto();

        if (!Fallback && pqs.quadAllowBuild)
        {
            foreach (var child in quad.subNodes)
            {
                if (child.isSubdivided)
                    continue;
                if (child.gcd1 >= pqs.visibleRadius)
                    continue;

                var build = new PendingBuild(this, child);
                if (!build.StartBuild())
                    continue;

                pending.Add(child, build);
            }
        }

        foreach (var child in quad.subNodes)
            child.UpdateVisibility();
    }

    public void OnQuadDestroy(PQ quad)
    {
        RemoveFromStorage(quad);

        if (!pending.TryGetValue(quad, out var build))
            return;

        using var guard = build;
        pending.Remove(quad);
    }

    /// <summary>
    /// Creates a <see cref="BatchPQSJobSet"/> by calling <see cref="BatchPQSMod.OnQuadPreBuild"/>
    /// on all mods for the given quad. The caller is responsible for disposing the returned job set.
    /// </summary>
    internal BatchPQSJobSet CreateJobSet(PQ quad)
    {
        var jobSet = BatchPQSJobSet.Acquire();
        foreach (var mod in mods)
            mod.OnQuadPreBuild(quad, jobSet);
        return jobSet;
    }

    #region Method Injections
    internal void PostSetupMods()
    {
        var fallbackMessage = new StringBuilder();
        Fallback = false;

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
                fallbackMessage.AppendLine(
                    $"PQSMod {mod.GetType().Name} is not supported by BatchPQS"
                );
                Fallback = true;
            }
        }

        this.mods = [.. batchMods];
        colliderMod = pqs.GetComponentInChildren<PQSMod_QuadMeshColliders>();

        if (!Fallback)
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
                    fallbackMessage.AppendLine(
                        $"PQSMod {mod.GetType().Name} is not supported by BatchPQS: {e.Message}"
                    );
                    Fallback = true;
                }
            }
        }

        if (Fallback)
        {
            Debug.LogWarning(
                $"[BurstPQS] BatchPQS not supported for surface {pqs.name}. Falling back to regular PQS"
            );

            FallbackMessage = fallbackMessage.ToString();
        }
        else
        {
            Debug.Log($"[BurstPQS] BatchPQS enabled for surface {pqs.name}");
            FallbackMessage = "This planet is supported by BurstPQS";
        }
    }
    #endregion

    struct PendingBuild(BatchPQS batchPQS, PQ quad) : IDisposable
    {
        readonly PQ quad = quad;
        readonly BatchPQS batchPQS = batchPQS;
        readonly PQS pqs => batchPQS.pqs;
        MeshData meshData;
        BatchPQSJobSet jobSet;
        JobHandle handle;

        public readonly bool IsCompleted => handle.IsCompleted;
        public readonly PQ Quad => quad;

        public bool StartBuild()
        {
            if (quad.isBuilt)
                return false;
            if (quad.isSubdivided)
                return false;

            if (quad == null || quad.gameObject == null)
                return false;

            pqs.buildQuad = quad;

            meshData = MeshData.Acquire();
            jobSet = BatchPQSJobSet.Acquire();
            foreach (var mod in batchPQS.mods)
                mod.OnQuadPreBuild(quad, jobSet);

            // This mirrors how PQ.SetupQuad builds quadMatrix, but in doubles.
            var planeRoot = quad.quadRoot != null ? quad.quadRoot : quad;
            var planeTransform =
                BurstUtil.RotationMatrix(planeRoot.planeRotation) * quad.quadScaleFactor;

            double4x4 planetToQuad = default;
            if (pqs.surfaceRelativeQuads && !batchPQS.TryGetPlanetToQuad(quad, out planetToQuad))
            {
                // Invert the matrix the quad is actually rendered with. Its
                // translation can differ from transform.position by up to a
                // float step at planet scale, so neither that nor the float
                // worldToLocalMatrix can be used here.
                var quadToWorld = new double4x4(
                    BurstUtil.ConvertMatrix(quad.transform.localToWorldMatrix)
                );
                var worldToQuad = math.inverse(quadToWorld);

                planetToQuad = math.mul(worldToQuad, batchPQS.GetPreciseLocalToWorld());
            }

            var job = new BuildQuadJob
            {
                quadPlanePosition = BurstUtil.ConvertVector(quad.positionPlanePosition),
                quadPlaneTransform = planeTransform,
                planetToQuad = planetToQuad,

                surfaceRelativeQuads = pqs.surfaceRelativeQuads,
                reqVertexMapCoords = pqs.reqVertexMapCoods,
                reqCustomNormals = pqs.reqCustomNormals,
                reqSphereUV = pqs.reqSphereUV,
                reqUVQuad = pqs.reqUVQuad,
                reqUV2 = pqs.reqUV2,
                reqUV3 = pqs.reqUV3,
                reqUV4 = pqs.reqUV4,
                reqBuildTangents = pqs.reqBuildTangents,
                reqAssignTangents = pqs.reqAssignTangents,
                reqColorChannel = pqs.reqColorChannel,

                uvSW = quad.uvSW,
                uvDelta = quad.uvDelta,

                cacheVertexCount = PQS.cacheVertCount,
                cacheSideVertCount = PQS.cacheSideVertCount,
                cacheMeshSize = PQS.cacheMeshSize,
                cacheRes = PQS.cacheRes,
                cacheTriCount = PQS.cacheTriCount,

                sphere = new(pqs),

                jobSet = new(jobSet),
                meshData = new(meshData),
                pq = new(quad),
            };

            handle = job.Schedule();

            return true;
        }

        static readonly VertexAttributeDescriptor[] AttrWithoutTangent =
        [
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 0),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, 0),
        ];

        static readonly VertexAttributeDescriptor[] AttrWithTangent =
        [
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 2),
            new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 0),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 0),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, 0),
        ];

        public void Complete()
        {
            var mesh = quad.mesh;
            mesh.Clear(false);
            handle.Complete();

            if (!meshData.interleaved.IsCreated)
                throw new Exception("mesh vertex data is empty");

            pqs.buildQuad = quad;

            int vertexCount = meshData.interleaved.Length;
            const MeshUpdateFlags flags =
                MeshUpdateFlags.DontValidateIndices
                | MeshUpdateFlags.DontResetBoneBounds
                | MeshUpdateFlags.DontNotifyMeshUsers;

            VertexAttributeDescriptor[] attrs = meshData.tangents.IsCreated
                ? AttrWithTangent
                : AttrWithoutTangent;

            mesh.SetVertexBufferParams(vertexCount, attrs);
            mesh.SetVertexBufferData(meshData.interleaved, 0, 0, vertexCount, 0, flags);
            mesh.SetVertexBufferData(meshData.normals, 0, 0, vertexCount, 1, flags);
            if (meshData.tangents.IsCreated)
                mesh.SetVertexBufferData(meshData.tangents, 0, 0, vertexCount, 2, flags);

            // Stitch edges now so the indices only need to be uploaded once.
            var edgeState = HasNeighbours(quad) ? quad.GetEdgeState() : PQS.EdgeState.Reset;
            var indices = PQS.cacheIndices[edgeState == PQS.EdgeState.Reset ? 0 : (int)edgeState];

            SetIndices(mesh, indices, meshData.bounds, vertexCount, flags);
            mesh.bounds = meshData.bounds;

            // Populate global PQS cache arrays that stock normally fills per-vertex.
            // These must be populated before OnMeshBuilt since stock PQSMods may read them.
            meshData.vertsD.CopyTo(PQS.verts);
            meshData.normals.CopyTo(PQS.normals);
            if (meshData.tangents.IsCreated)
                meshData.tangents.CopyTo(PQS.cacheTangents);
            meshData.cacheColors.CopyTo(PQS.cacheColors);
            meshData.cacheUVs.CopyTo(PQS.cacheUVs);
            meshData.cacheUV2s.CopyTo(PQS.cacheUV2s);
            meshData.cacheUV3s.CopyTo(PQS.cacheUV3s);
            meshData.cacheUV4s.CopyTo(PQS.cacheUV4s);

            quad.edgeState = edgeState;

            jobSet.OnMeshBuilt(quad);

            foreach (var mod in batchPQS.mods)
                mod.OnQuadBuilt(quad);
            pqs.buildQuad = null;

            // OnQuadBuilt mods may change fields used by the snapshot.
            batchPQS.InvalidateQuadSnapshot(quad);
        }

        public void Dispose()
        {
            try
            {
                handle.Complete();
            }
            finally
            {
                if (meshData is not null)
                {
                    if (batchPQS.disposeList.IsCreated)
                        batchPQS.disposeList.Add(meshData.ReleaseData());
                    else
                        meshData.Dispose();
                }
                jobSet?.Dispose();
            }
        }
    }
}
