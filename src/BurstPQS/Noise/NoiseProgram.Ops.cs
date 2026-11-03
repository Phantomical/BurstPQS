using BurstPQS.Util;
using Unity.Mathematics;
using static BurstPQS.Noise.BatchUtil;
using static BurstPQS.Noise.NoiseUtil;

namespace BurstPQS.Noise;

public unsafe partial struct NoiseProgram
{
    // The value and frame ops, as a double4 loop over whole groups of 4
    // followed by a scalar loop over the rest.
    static class Ops
    {
        public static void Constant(double* o, double value, int n)
        {
            for (int i = 0; i < n; ++i)
                o[i] = value;
        }

        public static void Abs(double* a, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, math.abs(Load4(a, i)));

            for (; i < n; ++i)
                a[i] = math.abs(a[i]);
        }

        public static void Bias(double* a, double bias, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Load4(a, i) + bias);

            for (; i < n; ++i)
                a[i] = a[i] + bias;
        }

        public static void Scale(double* a, double scale, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Load4(a, i) * scale);

            for (; i < n; ++i)
                a[i] = a[i] * scale;
        }

        public static void ScaleBias(double* a, double2 p, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, MathUtil.Mad(Load4(a, i), p.x, p.y));

            for (; i < n; ++i)
                a[i] = MathUtil.Mad(a[i], p.x, p.y);
        }

        public static void Clamp(double* a, double2 p, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Clamp(Load4(a, i), p.x, p.y));

            for (; i < n; ++i)
                a[i] = Clamp(a[i], p.x, p.y);
        }

        public static void Exponential(double* a, double exponent, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Exponential(Load4(a, i), exponent));

            for (; i < n; ++i)
                a[i] = Exponential(a[i], exponent);
        }

        public static void Negate(double* a, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, 0.0 - Load4(a, i));

            for (; i < n; ++i)
                a[i] = 0.0 - a[i];
        }

        public static void Add(double* a, double* b, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Load4(a, i) + Load4(b, i));

            for (; i < n; ++i)
                a[i] = a[i] + b[i];
        }

        public static void Multiply(double* a, double* b, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Load4(a, i) * Load4(b, i));

            for (; i < n; ++i)
                a[i] = a[i] * b[i];
        }

        public static void Max(double* a, double* b, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, math.max(Load4(a, i), Load4(b, i)));

            for (; i < n; ++i)
                a[i] = math.max(a[i], b[i]);
        }

        public static void Min(double* a, double* b, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, math.min(Load4(a, i), Load4(b, i)));

            for (; i < n; ++i)
                a[i] = math.min(a[i], b[i]);
        }

        public static void Power(double* a, double* b, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, math.pow(Load4(a, i), Load4(b, i)));

            for (; i < n; ++i)
                a[i] = math.pow(a[i], b[i]);
        }

        public static void Blend(double* a, double* b, double* w, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Lerp(Load4(a, i), Load4(b, i), MathUtil.Mad(Load4(w, i), 0.5, 0.5)));

            for (; i < n; ++i)
                a[i] = Lerp(a[i], b[i], MathUtil.Mad(w[i], 0.5, 0.5));
        }

        public static void Select(double* a, double* b, double* c, SelectData* p, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(a, i, Select(p, Load4(c, i), Load4(a, i), Load4(b, i)));

            for (; i < n; ++i)
                a[i] = Select(p, c[i], a[i], b[i]);
        }

        public static void Translate(Frame f, Frame o, double3 t, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
            {
                Store4(o.x, i, Load4(f.x, i) + t.x);
                Store4(o.y, i, Load4(f.y, i) + t.y);
                Store4(o.z, i, Load4(f.z, i) + t.z);
            }

            for (; i < n; ++i)
            {
                o.x[i] = f.x[i] + t.x;
                o.y[i] = f.y[i] + t.y;
                o.z[i] = f.z[i] + t.z;
            }
        }

        public static void ScaleInput(Frame f, Frame o, double3 s, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
            {
                Store4(o.x, i, Load4(f.x, i) * s.x);
                Store4(o.y, i, Load4(f.y, i) * s.y);
                Store4(o.z, i, Load4(f.z, i) * s.z);
            }

            for (; i < n; ++i)
            {
                o.x[i] = f.x[i] * s.x;
                o.y[i] = f.y[i] * s.y;
                o.z[i] = f.z[i] * s.z;
            }
        }

        public static void Rotate(Frame f, Frame o, RotateData* m, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
            {
                double4 x = Load4(f.x, i);
                double4 y = Load4(f.y, i);
                double4 z = Load4(f.z, i);
                Store4(o.x, i, MathUtil.Mad(m->x1, x, MathUtil.Mad(m->y1, y, m->z1 * z)));
                Store4(o.y, i, MathUtil.Mad(m->x2, x, MathUtil.Mad(m->y2, y, m->z2 * z)));
                Store4(o.z, i, MathUtil.Mad(m->x3, x, MathUtil.Mad(m->y3, y, m->z3 * z)));
            }

            for (; i < n; ++i)
            {
                double x = f.x[i];
                double y = f.y[i];
                double z = f.z[i];
                o.x[i] = MathUtil.Mad(m->x1, x, MathUtil.Mad(m->y1, y, m->z1 * z));
                o.y[i] = MathUtil.Mad(m->x2, x, MathUtil.Mad(m->y2, y, m->z2 * z));
                o.z[i] = MathUtil.Mad(m->x3, x, MathUtil.Mad(m->y3, y, m->z3 * z));
            }
        }

        public static void NegateInput(Frame f, Frame o, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
            {
                Store4(o.x, i, 0.0 - Load4(f.x, i));
                Store4(o.y, i, 0.0 - Load4(f.y, i));
                Store4(o.z, i, 0.0 - Load4(f.z, i));
            }

            for (; i < n; ++i)
            {
                o.x[i] = 0.0 - f.x[i];
                o.y[i] = 0.0 - f.y[i];
                o.z[i] = 0.0 - f.z[i];
            }
        }

        public static void Displace(double* o, double* x, double* v, int n)
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
                Store4(o, i, Load4(x, i) + Load4(v, i));

            for (; i < n; ++i)
                o[i] = x[i] + v[i];
        }

        public static void Displace(
            Frame f,
            Frame o,
            double* dx,
            double* dy,
            double* dz,
            double power,
            int n
        )
        {
            int i = 0;
            for (; i + 4 <= n; i += 4)
            {
                Store4(o.x, i, MathUtil.Mad(Load4(dx, i), power, Load4(f.x, i)));
                Store4(o.y, i, MathUtil.Mad(Load4(dy, i), power, Load4(f.y, i)));
                Store4(o.z, i, MathUtil.Mad(Load4(dz, i), power, Load4(f.z, i)));
            }

            for (; i < n; ++i)
            {
                o.x[i] = MathUtil.Mad(dx[i], power, f.x[i]);
                o.y[i] = MathUtil.Mad(dy[i], power, f.y[i]);
                o.z[i] = MathUtil.Mad(dz[i], power, f.z[i]);
            }
        }

        // The control point search depends on each value, so this stays scalar.
        public static void Terrace(double* a, TerraceData* p, int n)
        {
            for (int i = 0; i < n; ++i)
                a[i] = Terrace(p, a[i]);
        }

        static double Terrace(TerraceData* p, double value)
        {
            int count = p->Count;
            // The control points directly follow the header.
            double* points = (double*)(p + 1);

            int i = 0;
            while (i < count && !(value < points[i]))
                i++;

            int lo = math.clamp(i - 1, 0, count - 1);
            int hi = math.clamp(i, 0, count - 1);
            if (lo == hi)
                return points[hi];

            double a = points[lo];
            double b = points[hi];
            double t = (value - a) / (b - a);
            if (p->Invert)
            {
                t = 1.0 - t;
                (a, b) = (b, a);
            }
            t *= t;
            return LinearInterpolate(a, b, t);
        }

        static double4 Lerp(double4 a, double4 b, double4 t) => MathUtil.Mad(t, b - a, a);

        static double Lerp(double a, double b, double t) => MathUtil.Mad(t, b - a, a);

        // The lower bound wins if the bounds are inverted, like LibNoise.
        static double4 Clamp(double4 v, double lower, double upper) =>
            math.select(math.select(v, upper, v > upper), lower, v < lower);

        static double Clamp(double v, double lower, double upper) =>
            v < lower ? lower : (v > upper ? upper : v);

        static double4 Exponential(double4 v, double exponent) =>
            MathUtil.Mad(math.pow(math.abs((v + 1.0) * 0.5), exponent), 2.0, -1.0);

        static double Exponential(double v, double exponent) =>
            MathUtil.Mad(math.pow(math.abs((v + 1.0) * 0.5), exponent), 2.0, -1.0);

        static double4 Select(SelectData* p, double4 control, double4 a, double4 b)
        {
            double lower = p->Lower;
            double upper = p->Upper;
            double falloff = p->EdgeFalloff;

            if (falloff > 0.0)
            {
                double lo0 = lower - falloff;
                double lo1 = lower + falloff;
                double hi0 = upper - falloff;
                double hi1 = upper + falloff;
                double4 rise = Lerp(a, b, SCurve3((control - lo0) / (lo1 - lo0)));
                double4 fall = Lerp(b, a, SCurve3((control - hi0) / (hi1 - hi0)));

                // Applied from the last range to the first so the earliest
                // matching range wins, as in LibNoise.
                double4 r = math.select(a, fall, control < hi1);
                r = math.select(r, b, control < hi0);
                r = math.select(r, rise, control < lo1);
                return math.select(r, a, control < lo0);
            }

            return math.select(a, b, !(control < lower) & control <= upper);
        }

        static double Select(SelectData* p, double control, double a, double b)
        {
            double lower = p->Lower;
            double upper = p->Upper;
            double falloff = p->EdgeFalloff;

            if (falloff > 0.0)
            {
                double lo0 = lower - falloff;
                double lo1 = lower + falloff;
                double hi0 = upper - falloff;
                double hi1 = upper + falloff;
                if (control < lo0)
                    return a;
                if (control < lo1)
                    return Lerp(a, b, SCurve3((control - lo0) / (lo1 - lo0)));
                if (control < hi0)
                    return b;
                if (control < hi1)
                    return Lerp(b, a, SCurve3((control - hi0) / (hi1 - hi0)));
                return a;
            }

            return !(control < lower) && control <= upper ? b : a;
        }
    }
}
