# Adapting a PQSMod to work with BurstPQS

This guide explains how to write a BurstPQS adapter for an existing `PQSMod`.

## Overview

When compared with stock KSP, BurstPQS makes a few big changes to how terrain
quads are built:
- The work of actually building the quad is moved to a background thread.
- PQS mods process all vertices in a quad at once, instead of handling them
  one-by-one.

This, however, needs you to reimplement parts of your `PQSMod` so that they
can be run by BurstPQS. This is done by implementing a `BatchPQSMod` which
provides the required operations.

> [!INFO]
> You don't need to write an adapter for every `PQSMod`. They are only needed
> for mods that override one of:
> - `OnVertexBuildHeight`, or,
> - `OnVertexBuild`
>
> However, some `PQSMods` modify the mesh in `OnMeshBuild`. These don't need
> to be converted to a `BatchPQSMod` adapter, but performance will generally
> be better if you do.

In order to implement a mod adapter you need to:
1. Inherit from `BatchPQSMod<T>`, where `T` is the type of your `PQSMod`.
2. Annotate the type with `[BatchPQSMod(typeof(T))]`.
3. Have a constructor that takes an instance of your mod.

This guide will walk you through some examples showing how to do this.

> [!WARN]
> BurstPQS will automatically detect BatchPQSMods declared in assemblies that
> have a `KSPAssemblyDependency` on the `BurstPQS` assembly. Make sure that
> you have this, otherwise your BatchPQS mod adapters will not be picked up.

## BatchPQS Jobs
BatchPQS builds PQS quads on a background thread with multiple quads potentially
being built at the same time. To make this work, BurstPQS makes you define a
separate job struct that holds the state needed to build each individual quad.
You register these with a `BurstPQSJobSet` in the `OnQuadPreBuild` callback
and then the various trait methods on the job struct will be called, if the
trait is implemented.

There are 5 different interfaces that you can implement:
* `IBatchPQSHeightJob`: This is the equivalent of `OnVertexBuildHeight`.
* `IBatchPQSVertexJob`: This is the equivalent of `OnVertexBuild`, it runs
   after all calls to `BuildHeights` have finished.
* `IBatchPQSMeshJob`: This runs after all calls to `BuildVertices` have
   finished. It has access to the actual vertices, normals, and other attributes
   of the mesh itself.
* `IBatchPQSMeshBuilt`: This runs on the main thread once the mesh has been
   created. It replaces the `OnMeshBuild` callback for a PQSMod.
* `IDisposable`: Called on the main thread, allows you dispose of any resources
   that were used as part of the job.

Your job struct can implement any combination of these interfaces.

> [!INFO]
> You can also add a `[BurstCompile]` annotation to your job and the `BuildHeights`,
> `BuildVertices`, and `BuildMesh` calls will be burst-compiled automatically. Keep
> in mind that when you do this your job struct and methods will need to follow all
> the restrictions for burst compilation.

## A simple height mod
The simplest PQSMod is probaly `VertexHeightOffset`. All it does is add a constant
offset to the vertex height in the `OnVertexBuildHeight` callback. Its adapter in
BurstPQS looks like this:

```cs
// Ensures that the burst compiler can find BuildJob below
[BurstCompile]
// Registers this as the BatchPQSMod for PQSMod_VertexHeightOffset
[BatchPQSMod(typeof(PQSMod_VertexHeightOffset))]
// Note that we have a single-parameter constructor that takes the mod.
public class VertexHeightOffset(PQSMod_VertexHeightOffset mod)
    : BatchPQSMod<PQSMod_VertexHeightOffset>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        // Calls the original OnQuadPreBuild for the PQSMod.
        // Not needed in this case, but it is good to keep it around unless you
        // know you don't need it.
        base.OnQuadPreBuild(quad);

        // Registers BuildJob to be executed as part of the quad build job.
        jobSet.Add(new BuildJob { offset = mod.offset });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        public double offset;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            // Now we process the entire quad at once
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += offset;
        }
    }
}
```

## Using `MapSO`s
`MapSO` is a managed type, and while the stock `MapSO` actually supports being
used on a background thread, others do not. To make things work BurstPQS has a
`BurstMapSO` type you can use to read from a `MapSO` from within a build job.

Here's an example showing how `PQSMod_VertexHeightMap` gets adapted:
```cs
[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightMap))]
public class VertexHeightMap(PQSMod_VertexHeightMap mod)
    : BatchPQSMod<PQSMod_VertexHeightMap>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildHeightsJob
            {
                // Call BurstMapSO.Create in order to create the MapSO adapter
                heightMap = BurstMapSO.Create(mod.heightMap),
                heightMapOffset = mod.heightMapOffset,
                heightMapDeformity = mod.heightMapDeformity,
            }
        );
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob, IDisposable
    {
        public BurstMapSO heightMap;
        public double heightMapOffset;
        public double heightMapDeformity;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                // In the job you can call all the methods you would normally use.
                data.vertHeight[i] += heightMapOffset
                    + heightMapDeformity * heightMap.GetPixelFloat(data.u[i], data.v[i]);
            }
        }

        public void Dispose()
        {
            // Make sure to dispose of the BurstMapSO when you are done with it.
            heightMap.Dispose();
        }
    }
}
```

It is also possible to register your own custom `MapSO` adapters. That is covered
later in the Custom MapSO Adapters section.

## Using Noise Functions
Many stock PQSMods use noise. This is less common for modded PQSMods, but BurstPQS
provides burst-compatible versions of the noise modules regardless. If you aren't
using burst to compile your job struct then you can ignore these, but the bursted
versions are _much_ faster than the other ones.

An example of using a noise function is the stock `PQSMod_VertexVoronoi`. Here's how
it gets adapted for BurstPQS:

```cs
[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexVoronoi))]
public class VertexVoronoi(PQSMod_VertexVoronoi mod) : BatchPQSMod<PQSMod_VertexVoronoi>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(new BuildJob { voronoi = new(mod.voronoi), deformation = mod.deformation });
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSHeightJob
    {
        // Defined in the BurstPQS.Noise namespace
        public BurstVoronoi voronoi;
        public double deformation;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.vertHeight[i] += voronoi.GetValue(data.directionFromCenter[i]) * deformation;
        }
    }
}
```

> [!WARN]
> Most noise modules don't need to be disposed of. However, `BurstSimplex` takes
> a reference to a managed array and will cause memory leaks if not disposed of
> properly.

## Passing Data Between Stages
Sometimes you need to calculate some data in an earlier stage (like `BuildHeights`)
and then use it in a later stage (like `BuildVertices` or `BuildMesh`). 
This is simple, write to a field in your job struct and read it in a later stage.

Here's how the adapter for `PQSMod_RemoveQuadMap` does exactly that
```cs
[BurstCompile]
[BatchPQSMod(typeof(PQSMod_RemoveQuadMap))]
public class RemoveQuadMap(PQSMod_RemoveQuadMap mod)
    : BatchPQSMod<PQSMod_RemoveQuadMap>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                mod = new(mod),
                map = BurstMapSO.Create(mod.map),
                minHeight = mod.minHeight,
                maxHeight = mod.maxHeight,
            }
        );
    }
    
    [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob, IBatchPQSMeshBuiltJob, IDisposable
    {
        // We want to burst-compile this, but we also need access to the PQSMod
        // itself in later stages. ObjectHandle allows us to keep a reference to
        // it while keeping BuildJob fully unmanaged.
        public ObjectHandle<PQSMod_RemoveQuadMap> mod;
        public BurstMapSO map;
        public float minHeight;
        public float maxHeight;

        // we set this in BuildVertices and then read it in OnMeshBuilt
        bool quadVisible;

        public void BuildVertices(in BuildVerticesData data)
        {
            quadVisible = false;

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var height = map.GetPixelFloat(data.u[i], data.v[i]);
                if (height >= minHeight && height <= maxHeight)
                {
                    quadVisible = true;
                    break;
                }
            }
        }

        public void OnMeshBuilt(PQ quad)
        {
            mod.Target.quadVisible = quadVisible;
        }

        public void Dispose()
        {
            map.Dispose();
            mod.Dispose();
        }
    }
}
```

You might find that you need to pass per-vertex state across stages. You can
do this by allocating a `NativeArray` and using that to store your data. Be careful
about using the right allocator!
- If you are passing data within the job (i.e. between `BuildHeights`, `BuildVertices`,
  and `BuildMesh`) then you should allocate data using `Allocator.Temp`. It is faster
  and unity will automatically deallocate it when the job is completed.
- If you are passing data back to the main thread (i.e. to `OnMeshBuilt` or `Dispose`)
  then you _must_ use `Allocator.Persistent`. There is no guarantee that the job will
  be completed in under 4 frames, so `TempJob` allocations might get freed automatically
  before `OnMeshBuilt` or `Dispose` are called.

`PQSMod_TangentTextureRanges` is an example of of a PQSMod adapter that needs to pass
per-vertex data between stages.

```cs
[BurstCompile]
[BatchPQSMod(typeof(PQSMod_TangentTextureRanges))]
public class TangentTextureRanges(PQSMod_TangentTextureRanges mod)
    : BatchPQSMod<PQSMod_TangentTextureRanges>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                modulo = mod.modulo,
                lowStart = mod.lowStart,
                lowEnd = mod.lowEnd,
                highStart = mod.highStart,
                highEnd = mod.highEnd,
            }
        );
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSVertexJob, IBatchPQSMeshJob
    {
        public double modulo;
        public double lowStart;
        public double lowEnd;
        public double highStart;
        public double highEnd;

        NativeArray<float> tangentX;

        public void BuildVertices(in BuildVerticesData data)
        {
            // We can use Allocator.Temp here because BuildVertices and BuildMesh
            // both happen within the quad build job.
            tangentX = new NativeArray<float>(data.VertexCount, Allocator.Temp);

            for (int i = 0; i < data.VertexCount; ++i)
            {
                var height = data.vertHeight[i];
                var low = 1.0 - SmoothStep(lowStart, lowEnd, height);
                var high = SmoothStep(highStart, highEnd, height);
                var med = 1.0 - low - high;

                low = Math.Round(low * modulo);
                med = Math.Round(med * modulo) * 2.0;
                high = Math.Round(high * modulo) * 3.0;

                tangentX[i] = (float)(high + med + low);
            }
        }

        public void BuildMesh(in BuildMeshData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
                data.tangents[i].x = tangentX[i];
        }

        static double SmoothStep(double a, double b, double x)
        {
            var t = MathUtil.Clamp01((x - a) / (b - a));
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
```

## Modifying Mesh Data
Some stock PQSMods modify the final mesh in `OnMeshBuild` callbacks. By default,
these will work correctly with BurstPQS but they tend to be much slower then they
could be. You can usually convert these to do most of the compute in a `BuildMesh`
callback so that it happens in parallel, and only do the last part of assigning
the data back on the main thread.

`PQSMod_TangentTextureRanges` is an example of an adapter that does this. By default,
the stock version assigns `mesh.uv` and `mesh.uv2` in `OnMeshBuild`. The adapter
does that in the `BuildMesh` callback and adjusts `modRequirements` so that those
mesh channels are properly assigned.

```cs
[BurstCompile]
[BatchPQSMod(typeof(PQSMod_UVPlanetRelativePosition))]
public class UVPlanetRelativePosition(PQSMod_UVPlanetRelativePosition mod)
    : BatchPQSMod<PQSMod_UVPlanetRelativePosition>(mod)
{
    public override void OnSetup()
    {
        var pqs = mod.sphere;

        // We need these so that uv and uv2 are assigned to the mesh
        pqs.modRequirements |= PQS.ModiferRequirements.MeshUV2;
        pqs.modRequirements |= PQS.ModiferRequirements.UVQuadCoords;
    }

    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);
        jobSet.Add(new BuildJob());
    }

    public override void OnQuadBuilt(PQ quad)
    {
        // don't call into the default OnQuadBuilt since it is already handled
        // by BuildJob.BuildMesh.
    }

    [BurstCompile]
    struct BuildJob : IBatchPQSMeshJob
    {
        public readonly void BuildMesh(in BuildMeshData data)
        {
            for (int i = 0; i < data.VertexCount; ++i)
            {
                var v = data.vertsD[i];
                var n = data.normals[i];

                // overwrite uvs and uv2s, these will be assigned to the mesh
                // as part of a normal job build.
                data.uvs[i].x = (float)v.x;
                data.uvs[i].y = (float)v.y;
                data.uv2s[i].x = (float)v.z;
                double mag = Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
                var vn = mag > 0.0 ? v / mag : Vector3d.zero;
                data.uv2s[i].y = (float)(1.0 - Vector3d.Dot(vn, n));
            }
        }
    }
}
```

## Implementing a Custom MapSO Adapter
Some mods introduce their own derived `MapSO` types. Using these with BurstPQS
means that somebody needs to implement an adapter that makes them work with
`BurstMapSO`.

In order to implement an adapter for a MapSO you need to:
1. Create a new struct,
2. Implement `IMapSO` for your struct,
3. (optionally) implement `IDisposable` for your struct
4. Register a factory function for your adapter by calling
   ```cs
   BurstMapSO.RegisterMapSOFactoryFunc<YouMapSO>(mapSO => BurstMapSO.Create(new YourMapSOAdapter(mapSO)))
   ```
5. (optionally) add a `[BurstCompile]` annotation to you MapSO adapter struct so
   that it gets burst-compiled.

Most MapSO types wrap a texture. There are classes in `TextureMapSO` that can be
used to wrap pretty much all texture types used in KSP, and you can use these to
implement your MapSO adapter.
