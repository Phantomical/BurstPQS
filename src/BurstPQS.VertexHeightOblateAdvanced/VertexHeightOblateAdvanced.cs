using System;
using Unity.Burst;
using Unity.Mathematics;
using VertexHeightOblateAdvanced;
using OblateModes = VertexHeightOblateAdvanced.PQSMod_VertexHeightOblateAdvanced.OblateModes;

namespace BurstPQS.Duckweed;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightOblateAdvanced))]
class VertexHeightOblateAdvanced(PQSMod_VertexHeightOblateAdvanced mod)
    : BatchPQSMod<PQSMod_VertexHeightOblateAdvanced>(mod)
{
    public override void OnQuadPreBuild(PQ quad, BatchPQSJobSet jobSet)
    {
        base.OnQuadPreBuild(quad, jobSet);

        jobSet.Add(
            new BuildJob
            {
                oblateMode = mod.oblateMode,
                a = mod.a,
                b = mod.b,
                c = mod.c,
                criticality = mod.criticality,
                primaryRadius = mod.primaryRadius,
                primarySlope = mod.primarySlope,
                primarySlopeXLimit = mod.primarySlopeXLimit,
                secondaryRadius = mod.secondaryRadius,
                secondarySlope = mod.secondarySlope,
                secondarySlopeXLimit = mod.secondarySlopeXLimit,
            }
        );
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct BuildJob : IBatchPQSHeightJob
    {
        public OblateModes oblateMode;
        public double a;
        public double b;
        public double c;
        public double criticality;
        public double primaryRadius;
        public double primarySlope;
        public double primarySlopeXLimit;
        public double secondaryRadius;
        public double secondarySlope;
        public double secondarySlopeXLimit;

        public readonly void BuildHeights(in BuildHeightsData data)
        {
            switch (oblateMode)
            {
                case OblateModes.PointEquipotential:
                    if (criticality == 0.0)
                        break;

                    for (int i = 0; i < data.VertexCount; ++i)
                    {
                        double theta = Math.PI * data.v[i];
                        double deformity = CalculateDeformityPointEquipotential(theta);

                        data.vertHeight[i] += data.sphere.radius * (deformity - 1.0);
                    }

                    break;

                case OblateModes.Blend:
                    for (int i = 0; i < data.VertexCount; ++i)
                    {
                        double phi = 2.0 * Math.PI * data.u[i];
                        double theta = Math.PI * data.v[i];
                        double deformity =
                            CalculateDeformityPointEquipotential(theta)
                            * CalculateDeformityEllipsoid(phi, theta);

                        data.vertHeight[i] += data.sphere.radius * (deformity - 1.0);
                    }
                    break;

                case OblateModes.UniformEquipotential:
                case OblateModes.CustomEllipsoid:
                    for (int i = 0; i < data.VertexCount; ++i)
                    {
                        double phi = 2.0 * Math.PI * data.u[i];
                        double theta = Math.PI * data.v[i];
                        double deformity = CalculateDeformityEllipsoid(phi, theta);

                        data.vertHeight[i] += data.sphere.radius * (deformity - 1.0);
                    }
                    break;

                case OblateModes.ContactBinary:
                    for (int i = 0; i < data.VertexCount; ++i)
                    {
                        double phi = 2.0 * Math.PI * data.u[i];
                        double theta = Math.PI * data.v[i];
                        double deformity = CalculateDeformityContactBinary(phi, theta);

                        data.vertHeight[i] += data.sphere.radius * (deformity - 1.0);
                    }
                    break;
            }
        }

        readonly double CalculateDeformityPointEquipotential(double theta)
        {
            if (theta <= 0.0 || theta >= Math.PI || criticality == 0.0)
                return 1.0;

            double sintheta = math.sin(theta);
            double heightScaleFactor =
                3.0
                * math.cos((Math.PI + math.acos(criticality * sintheta)) / 3.0)
                / (criticality * sintheta);

            return math.clamp(heightScaleFactor, 1.0, 1.5);
        }

        readonly double CalculateDeformityEllipsoid(double phi, double theta)
        {
            math.sincos(new double2(phi, theta), out var sin, out var cos);

            double2 sin2 = sin * sin;
            double2 cos2 = cos * cos;

            double sin2theta = sin2.y;
            double cos2theta = cos2.y;
            double sin2phi = sin2.x;
            double cos2phi = cos2.x;

            double term1 = sin2theta * cos2phi / (a * a);
            double term2 = sin2theta * sin2phi / (b * b);
            double term3 = cos2theta / (c * c);

            return math.rsqrt(term1 + term2 + term3);
        }

        readonly double CalculateDeformityContactBinary(double phi, double theta)
        {
            double cosphi = math.cos(phi);
            double sintheta = math.sin(theta);

            if (
                (-Math.PI / 2 < phi && phi < Math.PI / 2)
                || (Math.PI * 1.5f < phi && phi < Math.PI * 2f)
            )
            {
                var denominator = 1 - ((1 + primarySlope) * Sqr(sintheta * cosphi));
                var rsqrtden = math.rsqrt(denominator);
                var xValue = sintheta * cosphi * rsqrtden;
                if (xValue < primarySlopeXLimit)
                    return rsqrtden;
                return 2 * primaryRadius * sintheta * cosphi;
            }

            if (
                (Math.PI / 2 < phi && phi < Math.PI * 1.5f)
                || (-Math.PI < phi && phi < -Math.PI / 2)
            )
            {
                var denominator = 1 - ((1 + secondarySlope) * Sqr(sintheta * cosphi));
                var rsqrtden = math.rsqrt(denominator);
                var xValue = sintheta * cosphi * rsqrtden;
                if (xValue > secondarySlopeXLimit)
                    return rsqrtden;
                return -2 * secondaryRadius * sintheta * cosphi;
            }

            return 1;
        }

        static double Sqr(double x) => x * x;
    }
}
