using System.Collections.Generic;

namespace BurstPQS;

public interface IBatchPQSMod
{
    void OnQuadBuildVertex(in QuadBuildData data);

    void OnQuadBuildVertexHeight(in QuadBuildData data);
}

public abstract class BatchPQSMod : IBatchPQSMod
{
    public virtual void OnSetup() { }

    public virtual void OnQuadBuildVertex(in QuadBuildData data) { }

    public virtual void OnQuadBuildVertexHeight(in QuadBuildData data) { }
}

public abstract class BatchPQSMod<T>(T mod) : BatchPQSMod
{
    protected T mod = mod;

    public T Mod => mod;
}
