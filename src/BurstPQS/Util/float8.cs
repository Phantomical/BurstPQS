using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86.Avx;

namespace BurstPQS.Util
{
    internal struct float8
    {
        // Stored as a v256 so Burst doesn't see a concatenation of two halves,
        // which makes LLVM split 256-bit shuffles into 128-bit ones.
        v256 value;

        public readonly float4 lo => new(value.Float0, value.Float1, value.Float2, value.Float3);
        public readonly float4 hi => new(value.Float4, value.Float5, value.Float6, value.Float7);

        float8(v256 value) => this.value = value;

        public float8(float4 lo, float4 hi)
            : this(new v256(lo.x, lo.y, lo.z, lo.w, hi.x, hi.y, hi.z, hi.w)) { }

        public float8(
            float e0,
            float e1,
            float e2,
            float e3,
            float e4,
            float e5,
            float e6,
            float e7
        )
            : this(new float4(e0, e1, e2, e3), new float4(e4, e5, e6, e7)) { }

        public float8(float value)
            : this(new float4(value), new float4(value)) { }

        public static implicit operator v256(float8 v) => v.value;

        public static implicit operator float8(v256 v) => new(v);

        public static implicit operator float8(float v) => new(v);

        public static float8 operator +(float8 a, float8 b)
        {
            if (IsAvxSupported)
                return mm256_add_ps(a, b);
            return new(a.lo + b.lo, a.hi + b.hi);
        }

        public static float8 operator -(float8 a, float8 b)
        {
            if (IsAvxSupported)
                return mm256_sub_ps(a, b);
            return new(a.lo - b.lo, a.hi - b.hi);
        }

        public static float8 operator *(float8 a, float8 b)
        {
            if (IsAvxSupported)
                return mm256_mul_ps(a, b);
            return new(a.lo * b.lo, a.hi * b.hi);
        }

        public static float8 operator -(float8 a)
        {
            if (IsAvxSupported)
                return mm256_xor_ps(a, new v256(-0f));
            return new(-a.lo, -a.hi);
        }

        public static bool8 operator <(float8 a, float8 b)
        {
            if (IsAvxSupported)
                return mm256_cmp_ps(a, b, (int)CMP.LT_OQ);
            return new(a.lo < b.lo, a.hi < b.hi);
        }

        public static bool8 operator >(float8 a, float8 b)
        {
            if (IsAvxSupported)
                return mm256_cmp_ps(a, b, (int)CMP.GT_OQ);
            return new(a.lo > b.lo, a.hi > b.hi);
        }

        public static bool8 operator <=(float8 a, float8 b)
        {
            if (IsAvxSupported)
                return mm256_cmp_ps(a, b, (int)CMP.LE_OQ);
            return new(a.lo <= b.lo, a.hi <= b.hi);
        }

        public static bool8 operator >=(float8 a, float8 b)
        {
            if (IsAvxSupported)
                return mm256_cmp_ps(a, b, (int)CMP.GE_OQ);
            return new(a.lo >= b.lo, a.hi >= b.hi);
        }
    }
}
