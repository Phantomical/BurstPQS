using System;
using Unity.Profiling;

namespace BurstPQS.Util;

internal struct SuspendProfileScope : IDisposable
{
    ProfilerMarker.AutoScope scope;

    public SuspendProfileScope(ProfilerMarker.AutoScope scope)
    {
        ProfilerMarker.Internal_End(scope.m_Ptr);
        this.scope = scope;
    }

    public void Dispose()
    {
        ProfilerMarker.Internal_Begin(scope.m_Ptr);
    }
}
