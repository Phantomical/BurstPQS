using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    // static readonly SharedStatic<BurstExceptionVTable> VTableStatic;
    // static ref BurstExceptionVTable VTable => ref VTableStatic.Data;
    static BurstExceptionVTable VTable;

    static BurstException()
    {
        // VTableStatic = SharedStatic<BurstExceptionVTable>.GetOrCreate<BurstExceptionVTable>();
        // VTableStatic.Data.Init();
        throw new NotImplementedException();
    }

    [ModuleInitializer]
    internal static void ModuleInit() { }

    public static void ThrowIndexOutOfRange()
    {
        if (!IsBurstCompiled)
            ThrowIndexOutOfRangeManaged();
        VTable.ThrowIndexOutOfRange.Invoke();

        // Indicate to LLVM that ThrowIndexOutOfRange does not return.
        Hint.Assume(false);
    }

    [BurstDiscard]
    static void ThrowIndexOutOfRangeManaged() => throw new IndexOutOfRangeException();

    public static void ThrowArgumentOutOfRange()
    {
        if (!IsBurstCompiled)
            ThrowArgumentOutOfRangeManaged();
        VTable.ThrowArgumentOutOfRange.Invoke();

        Hint.Assume(false);
    }

    [BurstDiscard]
    static void ThrowArgumentOutOfRangeManaged() => throw new ArgumentOutOfRangeException();
}
