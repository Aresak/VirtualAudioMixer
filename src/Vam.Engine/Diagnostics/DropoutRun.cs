namespace Vam.Engine.Diagnostics;

/// <summary>
/// One endpoint repeating one kind of dropout, counted for an interval rather than logged each time.
/// </summary>
/// <param name="startTicks">When the interval began, in UTC ticks.</param>
sealed class DropoutRun(long startTicks)
{
    public long StartTicks { get; set; } = startTicks;

    public long LastTicks { get; set; } = startTicks;

    public int Count { get; set; }

    public long Frames { get; set; }

    public void Add(DropoutRecord record)
    {
        Count++;
        Frames += record.Frames;
        LastTicks = Math.Max(LastTicks, record.TimestampTicks);
    }

    public void Restart(long ticks)
    {
        StartTicks = ticks;
        LastTicks = ticks;
        Count = 0;
        Frames = 0;
    }
}
