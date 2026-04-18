#define CRASH_ON_EXCEPTION

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackgroundResourceProcessing.Shim;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using UnityEngine;
using static BurstPQS.Util.BurstUtil;

namespace BurstPQS;

internal static class BurstException
{
    delegate void ThrowException0Delegate();
    delegate void ThrowIndexOutOfRangeDelegate(int index, int length);

    struct BurstExceptionVTable
    {
        public FunctionPointer<ThrowIndexOutOfRangeDelegate> ThrowIndexOutOfRange;
        public FunctionPointer<ThrowException0Delegate> ThrowArgumentOutOfRange;

        [BurstDiscard]
        public void Init()
        {
            ThrowIndexOutOfRange = new(
                Marshal.GetFunctionPointerForDelegate(ThrowIndexOutOfRangeManaged)
            );
            ThrowArgumentOutOfRange = new(
                Marshal.GetFunctionPointerForDelegate(ThrowArgumentOutOfRangeManaged)
            );
        }
    }

    static readonly SharedStaticShim<BurstExceptionVTable> VTableStatic;
    static ref BurstExceptionVTable VTable => ref VTableStatic.Data;

    static BurstException()
    {
        VTableStatic = SharedStaticShim<BurstExceptionVTable>.GetOrCreate<BurstExceptionVTable>();
        InitVTable();
    }

    [BurstDiscard]
    static void InitVTable()
    {
        VTableStatic.Data.Init();
    }

    [ModuleInitializer]
    internal static void ModuleInit() { }

    public static void ThrowIndexOutOfRange(int index, int length)
    {
        if (!IsBurstCompiled)
            ThrowIndexOutOfRangeManaged(index, length);
        ThrowIndexOutOfRangeImpl(index, length);

        // Indicate to LLVM that ThrowIndexOutOfRange does not return.
        Hint.Assume(false);
    }

    public static void ThrowIndexOutOfRange(int start, int count, int length)
    {
        if (start < 0 || start >= length)
            ThrowIndexOutOfRange(start, length);

        ThrowIndexOutOfRange(start + count, length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [IgnoreWarning(1370)]
    static void ThrowIndexOutOfRangeImpl(int index, int length)
    {
#if CRASH_ON_EXCEPTION
        Debug.LogError($"Array index {index} was out of range (expected 0 < {index} < {length})");
        throw new IndexOutOfRangeException();
#else
        VTable.ThrowIndexOutOfRange.Invoke(index, length);
#endif
    }

    [BurstDiscard]
    static void ThrowIndexOutOfRangeManaged(int index, int length) =>
        throw new IndexOutOfRangeException(
            $"Array index {index} was out of range (expected 0 < {index} < {length})"
        );

    public static void ThrowArgumentOutOfRange()
    {
        if (!IsBurstCompiled)
            ThrowArgumentOutOfRangeManaged();
        ThrowArgumentOutOfRangeImpl();

        Hint.Assume(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [IgnoreWarning(1370)]
    static void ThrowArgumentOutOfRangeImpl()
    {
#if CRASH_ON_EXCEPTION
        throw new ArgumentOutOfRangeException();
#else
        VTable.ThrowArgumentOutOfRange.Invoke();
#endif
    }

    [BurstDiscard]
    static void ThrowArgumentOutOfRangeManaged() => throw new ArgumentOutOfRangeException();
}
