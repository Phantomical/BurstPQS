namespace BurstPQS.Util;

public readonly struct BurstLerpRange(PQSLandControl.LerpRange range)
{
    public readonly double startStart = range.startStart;
    public readonly double startEnd = range.startEnd;
    public readonly double endStart = range.endStart;
    public readonly double endEnd = range.endEnd;
    public readonly double startDelta = range.startDelta;
    public readonly double endDelta = range.endDelta;

    public double Lerp(double point)
    {
        if (point <= startStart || point >= endEnd)
            return 0.0;
        if (point < startEnd)
            return (point - startStart) * startDelta;
        if (point <= endStart)
            return 1.0;
        if (point < endEnd)
            return 1.0 - (point - endStart) * endDelta;
        return 0.0;
    }
}
