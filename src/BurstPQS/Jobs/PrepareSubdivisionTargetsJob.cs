using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BurstPQS.Jobs;

struct SubdivisionTargetInput
{
    public double3 worldPosition;
    public double surfaceSpeed;
    public int colliderLevel;
    public double vesselRadius;
}

[BurstCompile]
struct PrepareSubdivisionTargetsJob : IJob
{
    // Above the safe orbit altitude the speed cap only has to keep the view
    // reasonable, so allow a quarter of the usual movement per frame (two
    // levels fewer at the same speed).
    const float OrbitalQuadLengthsScale = 0.25f;

    // Touching the ground this fast destroys the vessel anyway, and
    // Vessel.CheckKill catches anything that ends up below the terrain, so
    // the collider floor fades out across this range.
    const double ColliderFadeStartSpeed = 450.0;
    const double ColliderFadeEndSpeed = 550.0;

    // Above this speed, drop one level, then another per doubling of speed.
    const double DropLevelSpeed = 500.0;

    // KSP always runs fixed updates every 0.02 real seconds.
    const double RealFixedDeltaTime = 0.02;

    public SubdivisionTarget primary;
    public double primaryAngularSpeed;
    public double primarySurfaceSpeed;
    public double minOrbitalDistance;
    public bool limitSpeed;
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
        primary.maxLevelAtSpeed = GetMaxLevelAtSpeed(
            primaryAngularSpeed,
            math.length(primary.position),
            primarySurfaceSpeed,
            primary.colliderLevel
        );
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
                    position = position,
                    directionNormalized = position / distance,
                    collapseFactor = GetCollapseFactor(altitude),
                    maxLevelAtSpeed = GetMaxLevelAtSpeed(
                        angularSpeed,
                        distance,
                        input.surfaceSpeed,
                        input.colliderLevel
                    ),
                    colliderLevel = input.colliderLevel,
                    vesselRadius = input.vesselRadius,
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

    readonly int GetMaxLevelAtSpeed(
        double angularSpeed,
        double distance,
        double surfaceSpeed,
        int colliderLevel
    )
    {
        // Nothing can reach the terrain from above the safe orbit altitude, so
        // the collider floor isn't needed there.
        bool orbital = distance > minOrbitalDistance;
        var quadLengths = orbital
            ? maxQuadLengthsPerFrame * OrbitalQuadLengthsScale
            : maxQuadLengthsPerFrame;

        int level = GetSpeedLevel(angularSpeed, quadLengths);
        if (limitSpeed)
        {
            // Includes timewarp, since angularSpeed is per game-time fixed step.
            var effectiveSpeed = angularSpeed * distance / RealFixedDeltaTime;
            level = math.min(level, GetLinearSpeedLevel(effectiveSpeed));
        }

        if (orbital || level >= colliderLevel)
            return level;

        var fade = math.saturate(
            (surfaceSpeed - ColliderFadeStartSpeed)
                / (ColliderFadeEndSpeed - ColliderFadeStartSpeed)
        );
        return (int)math.round(math.lerp(colliderLevel, level, fade));
    }

    readonly int GetLinearSpeedLevel(double speed)
    {
        if (!(speed > DropLevelSpeed))
            return maxLevel;

        var drop = (int)math.floor(math.log2(speed / DropLevelSpeed)) + 1;
        return math.max(maxLevel - drop, minLevel);
    }

    readonly int GetSpeedLevel(double angularSpeed, double quadLengthsPerFrame)
    {
        if (quadLengthsPerFrame <= 0.0)
            return maxLevel;

        for (int level = maxLevel; level >= minLevel; level--)
        {
            if (angularSpeed < math.PI_DBL / 2.0 / math.pow(2.0, level) * quadLengthsPerFrame)
                return level;
        }

        return minLevel;
    }
}
