using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BurstPQS.Collections;

namespace BurstPQS.Async;

[AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
internal readonly struct ValueTask : IEquatable<ValueTask>
{
    readonly Task _task;

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _task?.IsCompleted ?? true;
    }
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
    public bool IsFaulted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _task?.IsFaulted ?? false;
    }
    public bool IsCanceled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _task?.IsCanceled ?? false;
    }

    public TaskStatus Status => _task?.Status ?? TaskStatus.RanToCompletion;

    public static ValueTask CompletedTask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ValueTask other) => _task == other._task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ValueTask lhs, ValueTask rhs) => lhs.Equals(rhs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ValueTask lhs, ValueTask rhs) => !lhs.Equals(rhs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task AsTask() => _task ?? Task.CompletedTask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTaskAwaiter GetAwaiter() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AsyncValueTaskMethodBuilder CreateAsyncMethodBuilder() =>
        AsyncValueTaskMethodBuilder.Create();

    internal static ValueTask WhenAll(FixedArray4<ValueTask> tasks) =>
        WhenAll(tasks.v0, tasks.v1, tasks.v2, tasks.v3);

    internal static ValueTask WhenAll(params Span<ValueTask> tasks)
    {
        int count = 0;
        for (int i = 0; i < tasks.Length; ++i)
        {
            ref var task = ref tasks[i];

            switch (task.Status)
            {
                case TaskStatus.RanToCompletion:
                    break;

                case TaskStatus.Canceled:
                case TaskStatus.Faulted:
                    task.GetAwaiter().GetResult();
                    break;

                default:
                    count += 1;
                    break;
            }
        }

        if (count == 0)
            return CompletedTask;

        var array = new Task[count];
        for (int j = 0, i = 0; i < tasks.Length; ++i)
        {
            ref var task = ref tasks[i];

            switch (task.Status)
            {
                case TaskStatus.RanToCompletion:
                case TaskStatus.Canceled:
                case TaskStatus.Faulted:
                    break;

                default:
                    array[j++] = task.AsTask();
                    break;
            }
        }

        return new(Task.WhenAll(array));
    }

    private static void ThrowTaskNullException() => throw new ArgumentNullException("task");
}

internal readonly struct ValueTaskAwaiter : INotifyCompletion, ICriticalNotifyCompletion
{
    private readonly ValueTask _value;

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value.IsCompleted;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueTaskAwaiter(ValueTask value) => _value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult()
    {
        if (_useBuilder)
            _methodBuilder.SetResult();
        else
            _haveResult = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception) => _methodBuilder.SetException(exception);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
