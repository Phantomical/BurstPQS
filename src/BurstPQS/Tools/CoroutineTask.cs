using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace BurstPQS.Tools;

/// <summary>
/// Runs a coroutine, stepping through any nested <see cref="IEnumerator"/>s itself, so that an
/// exception aborts the whole chain instead of just the innermost coroutine.
/// </summary>
internal sealed class CoroutineTask : CustomYieldInstruction
{
    readonly Stack<IEnumerator> frames = new();

    public bool IsCompleted { get; private set; }
    public Exception Error { get; private set; }

    public override bool keepWaiting => !IsCompleted;

    CoroutineTask(IEnumerator routine) => frames.Push(routine);

    public static CoroutineTask Start(MonoBehaviour host, IEnumerator routine)
    {
        var task = new CoroutineTask(routine);
        host.StartCoroutine(task.Run());
        return task;
    }

    /// <summary>
    /// Rethrows the first error among <paramref name="tasks"/> and logs the rest, which would
    /// otherwise be lost.
    /// </summary>
    public static void ThrowIfAnyFailed(IEnumerable<CoroutineTask> tasks)
    {
        Exception first = null;
        foreach (var task in tasks)
        {
            if (task.Error is null)
                continue;

            if (first is null)
                first = task.Error;
            else
                Debug.LogException(task.Error);
        }

        if (first is not null)
            ExceptionDispatchInfo.Capture(first).Throw();
    }

    IEnumerator Run()
    {
        while (frames.Count > 0)
        {
            var frame = frames.Peek();

            bool more;
            try
            {
                more = frame.MoveNext();
            }
            catch (Exception e)
            {
                // A compiler-generated iterator has already disposed itself on the way out.
                frames.Pop();
                Abort(e);
                yield break;
            }

            if (!more)
            {
                frames.Pop();
                continue;
            }

            if (frame.Current is IEnumerator nested)
            {
                frames.Push(nested);
                continue;
            }

            yield return frame.Current;
        }

        IsCompleted = true;
    }

    void Abort(Exception error)
    {
        Error = error;

        while (frames.Count > 0)
        {
            if (frames.Pop() is not IDisposable frame)
                continue;

            try
            {
                frame.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        IsCompleted = true;
    }
}
