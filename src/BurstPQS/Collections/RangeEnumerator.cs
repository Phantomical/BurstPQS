using System;
using System.Collections;
using System.Collections.Generic;

namespace BurstPQS.Collections;

public struct RangeEnumerator(int start, int stop) : IEnumerator<int>
{
    int current = start - 1;
    readonly int stop = stop;

    public readonly int Current => current;
    readonly object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        current += 1;
        return current < stop;
    }

    void IEnumerator.Reset() => throw new NotSupportedException();

    public void Dispose() { }
}
