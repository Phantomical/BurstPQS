using System;
using System.Collections.Generic;

namespace BurstPQS.Mod;

/// <summary>
/// This is a special shim type that forwards the callbacks from
/// <see cref="BatchPQSMod"/> to the equivalent per-vertex ones on
/// <see cref="PQSMod"/>.
///
/// It does not forward any of the other callbacks and shouldn't be used outside
/// of forwarding batched callbacks.
/// </summary>
internal sealed class Shim : BatchPQSMod
{
    struct OverrideInfo
    {
        public bool onVertexBuildOverridden;
        public bool onVertexBuildHeightOverridden;
    }

    readonly PQSMod mod;
    readonly OverrideInfo info;

    Shim(PQSMod mod, OverrideInfo info)
    {
        this.mod = mod;
        this.info = info;
    }

    internal static new Shim Create(PQSMod mod)
    {
        var info = GetOverrideInfo(mod.GetType());
        if (!info.onVertexBuildOverridden && !info.onVertexBuildHeightOverridden)
            return null;

        return new(mod, info);
    }

    #region BatchPQSMod
    public override void OnBatchVertexBuild(in QuadBuildData data)
    {
        if (!info.onVertexBuildOverridden)
            return;

        mod.overrideQuadBuildCheck = false;

        var vbdata = PQS.vbData;
        int vcount = data.VertexCount;

        for (int i = 0; i < vcount; ++i)
        {
            data.CopyTo(vbdata, i);
            mod.OnVertexBuild(vbdata);
            data.CopyFrom(vbdata, i);
        }
    }

    public override void OnBatchVertexBuildHeight(in QuadBuildData data)
    {
        if (!info.onVertexBuildHeightOverridden)
            return;

        mod.overrideQuadBuildCheck = false;

        var vbdata = PQS.vbData;
        int vcount = data.VertexCount;

        for (int i = 0; i < vcount; ++i)
        {
            data.CopyTo(vbdata, i);
            mod.OnVertexBuildHeight(vbdata);
            data.CopyFrom(vbdata, i);
        }
    }
    #endregion

    #region Override Info
    static readonly Dictionary<Type, OverrideInfo> OverrideInfoCache = [];

    static OverrideInfo GetOverrideInfo(Type type)
    {
        if (OverrideInfoCache.TryGetValue(type, out var info))
            return info;

        var onVertexBuild = type.GetMethod("OnVertexBuild", [typeof(PQS.VertexBuildData)]);
        var onVertexBuildHeight = type.GetMethod(
            "OnVertexBuildHeight",
            [typeof(PQS.VertexBuildData)]
        );

        info = new()
        {
            onVertexBuildOverridden = onVertexBuild.DeclaringType != typeof(PQSMod),
            onVertexBuildHeightOverridden = onVertexBuildHeight.DeclaringType != typeof(PQSMod),
        };
        OverrideInfoCache[type] = info;
        return info;
    }
    #endregion
}
