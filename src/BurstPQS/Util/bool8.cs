using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86.Avx;

namespace BurstPQS.Util
{
    // Lanes are all ones or all zeros so this matches an AVX compare mask.
    internal struct bool8
    {
        // Stored as a v256 so Burst doesn't see a concatenation of two halves,
        // which makes LLVM split 256-bit shuffles into 128-bit ones.
        v256 value;

        public readonly int4 lo => new(value.SInt0, value.SInt1, value.SInt2, value.SInt3);
        public readonly int4 hi => new(value.SInt4, value.SInt5, value.SInt6, value.SInt7);

        bool8(v256 value) => this.value = value;

        public bool8(int4 lo, int4 hi)
            : this(new v256(lo.x, lo.y, lo.z, lo.w, hi.x, hi.y, hi.z, hi.w)) { }

        public bool8(bool4 lo, bool4 hi)
            : this(math.select(0, -1, lo), math.select(0, -1, hi)) { }

        public bool8(bool value)
            : this(new int4(value ? -1 : 0), new int4(value ? -1 : 0)) { }

        public static implicit operator v256(bool8 v) => v.value;

        public static implicit operator bool8(v256 v) => new(v);

        public static implicit operator bool8(bool v) => new(v);

        public static bool8 operator &(bool8 a, bool8 b)
        {
            if (IsAvxSupported)
                return mm256_and_ps(a, b);
            return new(a.lo & b.lo, a.hi & b.hi);
        }

        public static bool8 operator |(bool8 a, bool8 b)
        {
            if (IsAvxSupported)
                return mm256_or_ps(a, b);
            return new(a.lo | b.lo, a.hi | b.hi);
        }

        public static bool8 operator ^(bool8 a, bool8 b)
        {
            if (IsAvxSupported)
                return mm256_xor_ps(a, b);
            return new(a.lo ^ b.lo, a.hi ^ b.hi);
        }

        public static bool8 operator !(bool8 a)
        {
            if (IsAvxSupported)
                return mm256_xor_ps(a, new v256(-1));
            return new(~a.lo, ~a.hi);
        }
    }
}
