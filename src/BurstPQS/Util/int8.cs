using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86.Avx2;

namespace BurstPQS.Util
{
    internal struct int8
    {
        public int4 lo;
        public int4 hi;

        public int8(int4 lo, int4 hi)
        {
            this.lo = lo;
            this.hi = hi;
        }

        public int8(int e0, int e1, int e2, int e3, int e4, int e5, int e6, int e7)
            : this(new int4(e0, e1, e2, e3), new int4(e4, e5, e6, e7)) { }

        public int8(int value)
            : this(new int4(value), new int4(value)) { }

        public static implicit operator v256(int8 v) =>
            new(v.lo.x, v.lo.y, v.lo.z, v.lo.w, v.hi.x, v.hi.y, v.hi.z, v.hi.w);

        public static implicit operator int8(v256 v) =>
            new(
                new int4(v.SInt0, v.SInt1, v.SInt2, v.SInt3),
                new int4(v.SInt4, v.SInt5, v.SInt6, v.SInt7)
            );

        public static implicit operator int8(int v) => new(v);

        public static int8 operator +(int8 a, int8 b)
        {
            if (IsAvx2Supported)
                return mm256_add_epi32(a, b);
            return new(a.lo + b.lo, a.hi + b.hi);
        }

        public static int8 operator -(int8 a, int8 b)
        {
            if (IsAvx2Supported)
                return mm256_sub_epi32(a, b);
            return new(a.lo - b.lo, a.hi - b.hi);
        }

        public static int8 operator *(int8 a, int8 b)
        {
            if (IsAvx2Supported)
                return mm256_mullo_epi32(a, b);
            return new(a.lo * b.lo, a.hi * b.hi);
        }

        public static int8 operator &(int8 a, int8 b)
        {
            if (IsAvx2Supported)
                return mm256_and_si256(a, b);
            return new(a.lo & b.lo, a.hi & b.hi);
        }

        public static int8 operator |(int8 a, int8 b)
        {
            if (IsAvx2Supported)
                return mm256_or_si256(a, b);
            return new(a.lo | b.lo, a.hi | b.hi);
        }

        public static int8 operator ^(int8 a, int8 b)
        {
            if (IsAvx2Supported)
                return mm256_xor_si256(a, b);
            return new(a.lo ^ b.lo, a.hi ^ b.hi);
        }

        // Arithmetic, and masked to 0-31 like C#. The count goes in a register so it
        // doesn't have to be a compile-time constant; LLVM still folds constant
        // counts to the immediate form.
        public static int8 operator >>(int8 a, int n)
        {
            if (IsAvx2Supported)
                return mm256_sra_epi32(a, new v128(n & 31, 0, 0, 0));
            return new(a.lo >> n, a.hi >> n);
        }

        public static int8 operator <<(int8 a, int n)
        {
            if (IsAvx2Supported)
                return mm256_sll_epi32(a, new v128(n & 31, 0, 0, 0));
            return new(a.lo << n, a.hi << n);
        }
    }
}
