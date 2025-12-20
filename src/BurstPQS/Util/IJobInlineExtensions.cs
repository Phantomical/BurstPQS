using Unity.Jobs;

namespace BurstPQS.Util;

public static class IJobInlineExtensions
{
    public static JobHandle ExecuteInline<T>(this T job, JobHandle dependsOn = default)
        where T : IJob
    {
        dependsOn.Complete();
        job.Execute();
        return dependsOn;
    }
}
