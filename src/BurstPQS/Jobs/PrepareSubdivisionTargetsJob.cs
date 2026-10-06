using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BurstPQS.Jobs;

struct SubdivisionTargetInput
{
    public double3 worldPosition;
    public double surfaceRadius;
    public double surfaceSpeed;
    public int colliderLevel;
}

[BurstCompile]
struct PrepareSubdivisionTargetsJob : IJob
{
    public SubdivisionTarget primary;
    public double4x4 planetToWorld;
    public double radius;
    public double maxDetailDistance;
    public double collapseSeaLevelValue;
    public double collapseAltitudeValue;
    public double collapseDelta;
    public float maxQuadLengthsPerFrame;
    public double fixedDeltaTime;
    public int minLevel;
    public int maxLevel;

    public NativeList<SubdivisionTargetInput> inputs;

    public NativeList<SubdivisionTarget> targets;

    public void Execute()
    {
        targets.Add(primary);

        var worldToPlanet = math.inverse(planetToWorld);

        foreach (var input in inputs)
        {
            var position = math.mul(worldToPlanet, new double4(input.worldPosition, 1.0)).xyz;
            var distance = math.length(position);
            if (distance == 0.0)
                continue;

            var altitude = distance - radius;
            if (altitude > maxDetailDistance * radius)
                continue;

            // Stock measures angular speed as the angle moved per fixed update.
            var angularSpeed = input.surfaceSpeed * fixedDeltaTime / distance;

            targets.Add(
                new SubdivisionTarget
                {
                    directionNormalized = position / distance,
                    absHeight = math.abs(distance - input.surfaceRadius),
                    collapseFactor = GetCollapseFactor(altitude),
                    maxLevelAtSpeed = math.max(
                        GetMaxLevelAtSpeed(angularSpeed),
                        input.colliderLevel
                    ),
                }
            );
        }

        inputs.Dispose();
    }

    // These mirror the per-target parts of PQS.UpdateVisual.
    readonly double GetCollapseFactor(double altitude)
    {
        var factor = collapseSeaLevelValue + altitude * collapseDelta;
        if (factor < collapseSeaLevelValue)
            return collapseSeaLevelValue;
        if (factor > collapseAltitudeValue)
            return collapseAltitudeValue;
        return factor;
    }

    readonly int GetMaxLevelAtSpeed(double angularSpeed)
    {
        if (maxQuadLengthsPerFrame <= 0f)
            return maxLevel;

        for (int level = maxLevel; level >= minLevel; level--)
        {
            if (angularSpeed < math.PI_DBL / 2.0 / math.pow(2.0, level) * maxQuadLengthsPerFrame)
                return level;
        }

        return minLevel;
    }
}
