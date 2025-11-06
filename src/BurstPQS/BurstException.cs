using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackgroundResourceProcessing.Shim;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using static BurstPQS.Util.BurstUtil;

namespace BurstPQS;

internal static class BurstException
{
    delegate void ThrowException0Delegate();

    struct BurstExceptionVTable
    {
        public FunctionPointer<ThrowException0Delegate> ThrowIndexOutOfRange;
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

    static readonly SharedStatic<BurstExceptionVTable> VTableStatic;
    static ref BurstExceptionVTable VTable => ref VTableStatic.Data;

    static BurstException()
    {
        VTableStatic = SharedStatic<BurstExceptionVTable>.GetOrCreate<BurstExceptionVTable>();
        InitVTable();
    }

    [BurstDiscard]
    static void InitVTable()
    {
        VTableStatic.Data.Init();
    }

    [ModuleInitializer]
    internal static void ModuleInit() { }

    public static void ThrowIndexOutOfRange()
    {
        if (!IsBurstCompiled)
            ThrowIndexOutOfRangeManaged();
        ThrowIndexOutOfRangeImpl();

        // Indicate to LLVM that ThrowIndexOutOfRange does not return.
        Hint.Assume(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowIndexOutOfRangeImpl()
    {
        VTable.ThrowIndexOutOfRange.Invoke();
    }

    [BurstDiscard]
    static void ThrowIndexOutOfRangeManaged() => throw new IndexOutOfRangeException();

    public static void ThrowArgumentOutOfRange()
    {
        if (!IsBurstCompiled)
            ThrowArgumentOutOfRangeManaged();
        ThrowArgumentOutOfRangeImpl();

        Hint.Assume(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ThrowArgumentOutOfRangeImpl()
    {
        VTable.ThrowArgumentOutOfRange.Invoke();
    }

    [BurstDiscard]
    static void ThrowArgumentOutOfRangeManaged() => throw new ArgumentOutOfRangeException();
}
