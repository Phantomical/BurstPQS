using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BurstPQS.Collections;

namespace BurstPQS.Async;

[AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
internal readonly struct ValueTask : IEquatable<ValueTask>
{
    readonly Task _task;

    public bool IsCompleted => _task?.IsCompleted ?? true;
    public bool IsCompletedSuccessfully
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_task is null)
                return true;

            return _task.Status == TaskStatus.RanToCompletion;
        }
    }
    public bool IsFaulted => _task?.IsFaulted ?? false;
    public bool IsCanceled => _task?.IsCanceled ?? false;

    public static ValueTask CompletedTask => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask(Task task)
    {
        if (task is null)
            ThrowTaskNullException();

        _task = task;
    }

    public override int GetHashCode() => _task?.GetHashCode() ?? 0;

    public override bool Equals(object obj)
    {
        if (obj is ValueTask vt)
            return Equals(vt);

        return false;
    }

    public bool Equals(ValueTask other) => _task == other._task;

    public static bool operator ==(ValueTask lhs, ValueTask rhs) => lhs.Equals(rhs);

    public static bool operator !=(ValueTask lhs, ValueTask rhs) => !lhs.Equals(rhs);

    public Task AsTask() => _task ?? Task.CompletedTask;

    public ValueTaskAwaiter GetAwaiter() => new(this);

    public static AsyncValueTaskMethodBuilder CreateAsyncMethodBuilder() =>
        AsyncValueTaskMethodBuilder.Create();

    internal static async ValueTask WhenAll(FixedArray4<ValueTask> tasks)
    {
        await tasks[0];
        await tasks[1];
        await tasks[2];
        await tasks[3];
    }

    internal static async ValueTask WhenAll(ValueTask[] tasks)
    {
        foreach (var task in tasks)
            await task;
    }

    private static void ThrowTaskNullException() => throw new ArgumentNullException("task");
}

internal readonly struct ValueTaskAwaiter : INotifyCompletion, ICriticalNotifyCompletion
{
    private readonly ValueTask _value;

    public bool IsCompleted => _value.IsCompleted;

    internal ValueTaskAwaiter(ValueTask value) => _value = value;

    public void GetResult()
    {
        if (_value.IsCompletedSuccessfully)
            return;

        _value.AsTask().GetAwaiter().GetResult();
    }

    public void OnCompleted(Action continuation)
    {
        _value
            .AsTask()
            .ConfigureAwait(continueOnCapturedContext: true)
            .GetAwaiter()
            .OnCompleted(continuation);
    }

    public void UnsafeOnCompleted(Action continuation)
    {
        _value
            .AsTask()
            .ConfigureAwait(continueOnCapturedContext: true)
            .GetAwaiter()
            .UnsafeOnCompleted(continuation);
    }
}

internal struct AsyncValueTaskMethodBuilder
{
    private AsyncTaskMethodBuilder _methodBuilder;
    private bool _haveResult;
    private bool _useBuilder;

    public ValueTask Task
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_haveResult)
                return default;

            _useBuilder = true;
            return new(_methodBuilder.Task);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AsyncValueTaskMethodBuilder Create()
    {
        // AsyncTaskMethodBuilder's Create method just returns default, so we can avoid extra
        // overhead here by doing the same.
        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        // This replicates the behaviour of AsyncTaskMethodBuilder without switching ExecutionContexts
        if (stateMachine is null)
            ThrowStateMachineIsNull();

        stateMachine.MoveNext();
    }

    private static void ThrowStateMachineIsNull() =>
        throw new ArgumentNullException("stateMachine");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStateMachine(IAsyncStateMachine stateMachine) =>
        _methodBuilder.SetStateMachine(stateMachine);

    public void SetResult()
    {
        if (_useBuilder)
            _methodBuilder.SetResult();
        else
            _haveResult = true;
    }

    public void SetException(Exception exception) => _methodBuilder.SetException(exception);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine
    )
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        _useBuilder = true;
        _methodBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine
    )
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        _useBuilder = true;
        _methodBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}
