using System;
using Unity.Burst;
using VertexHeightOblateAdvanced;
using OblateModes = VertexHeightOblateAdvanced.PQSMod_VertexHeightOblateAdvanced.OblateModes;

namespace BurstPQS.Duckweed;

[BurstCompile]
[BatchPQSMod(typeof(PQSMod_VertexHeightOblateAdvanced))]
public class VertexHeightOblateAdvanced(PQSMod_VertexHeightOblateAdvanced mod)
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

        public void BuildHeights(in BuildHeightsData data)
        {
            double aSqr = a * a;
            double bSqr = b * b;
            double cSqr = c * c;

            switch (oblateMode)
            {
                case OblateModes.PointEquipotential:
                    for (int i = 0; i < data.VertexCount; ++i)
                    {
                        double phi = 2.0 * Math.PI * data.u[i];
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
                            * DuckMathUtils.CalculateDeformityEllipsoid(
                                phi,
                                theta,
                                aSqr,
                                bSqr,
                                cSqr
                            );

                        data.vertHeight[i] += data.sphere.radius * (deformity - 1.0);
                    }
                    break;

                case OblateModes.UniformEquipotential:
                case OblateModes.CustomEllipsoid:
                    for (int i = 0; i < data.VertexCount; ++i)
                    {
                        double phi = 2.0 * Math.PI * data.u[i];
                        double theta = Math.PI * data.v[i];
                        double deformity = DuckMathUtils.CalculateDeformityEllipsoid(
                            phi,
                            theta,
                            aSqr,
                            bSqr,
                            cSqr
                        );

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

        // The equivalent of this method in DuckMathUtils includes a try catch
        // block which burst does not support, so we duplicate it here.
        readonly double CalculateDeformityPointEquipotential(double theta)
        {
            if (theta <= 0.0 || theta >= Math.PI || criticality == 0.0)
                return 1.0;

            double sintheta = Math.Sin(theta);
            double num =
                3.0
                * Math.Cos((Math.PI + Math.Acos(criticality * sintheta)) / 3.0)
                / (criticality * sintheta);

            if (num > 1.5)
                return 1.5;

            if (num < 1.0)
                return 1.0;

            return num;
        }

        readonly double CalculateDeformityContactBinary(double phi, double theta)
        {
            double num = 1.0;
            if (
                (-Math.PI / 2.0 < phi && phi < Math.PI / 2.0)
                || (4.71238898038469 < phi && phi < Math.PI * 2.0)
            )
            {
                num = 1.0 - (1.0 + primarySlope) * Math.Pow(Math.Sin(theta) * Math.Cos(phi), 2.0);
                if (Math.Sin(theta) * Math.Cos(phi) / Math.Sqrt(num) < primarySlopeXLimit)
                {
                    return Math.Sqrt(1.0 / num);
                }

                return 2.0 * primaryRadius * Math.Sin(theta) * Math.Cos(phi);
            }

            if (
                (Math.PI / 2.0 < phi && phi < 4.71238898038469)
                || (-Math.PI < phi && phi < -Math.PI / 2.0)
            )
            {
                num = 1.0 - (1.0 + secondarySlope) * Math.Pow(Math.Sin(theta) * Math.Cos(phi), 2.0);
                if (Math.Sin(theta) * Math.Cos(phi) / Math.Sqrt(num) > secondarySlopeXLimit)
                {
                    return Math.Sqrt(1.0 / num);
                }

                return -2.0 * secondaryRadius * Math.Sin(theta) * Math.Cos(phi);
            }

            return 1.0;
        }
    }
}
