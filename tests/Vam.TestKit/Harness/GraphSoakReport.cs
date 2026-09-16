namespace Vam.TestKit.Harness;

/// <summary>
/// What a soak found. I5.
/// </summary>
/// <remarks>
/// Returned rather than asserted, so the caller decides what counts as a failure and can print the
/// numbers whether or not it passed. A soak that only says pass or fail hides the run where the
/// worst callback went from a tenth of a block to nine tenths and still technically passed.
/// </remarks>
/// <param name="Simulated">How much audio was pushed through.</param>
/// <param name="Blocks">How many blocks that was.</param>
/// <param name="Channels">How many strips were running.</param>
/// <param name="AudioThreadBytes">What the render allocated. The number that has to be zero.</param>
/// <param name="WorstCallbackFraction">The worst block, as a fraction of its deadline.</param>
/// <param name="Overruns">Blocks that took longer than a block.</param>
/// <param name="Disturbances">Configuration changes applied while it ran.</param>
/// <param name="Wall">How long it actually took.</param>
public readonly record struct GraphSoakReport(
    TimeSpan Simulated,
    long Blocks,
    int Channels,
    long AudioThreadBytes,
    double WorstCallbackFraction,
    long Overruns,
    int Disturbances,
    TimeSpan Wall)
{
    /// <summary>How much faster than realtime it ran.</summary>
    public double Speed => Wall.TotalSeconds <= 0 ? 0 : Simulated.TotalSeconds / Wall.TotalSeconds;

    /// <summary>One line, for a log or a test's output.</summary>
    /// <returns>The line.</returns>
    public override string ToString() =>
        $"{Simulated.TotalHours:0.##} h of {Channels} channels in {Wall.TotalSeconds:0.#} s "
        + $"({Speed:0}x realtime, {Blocks} blocks, {Disturbances} changes): "
        + $"audio thread allocated {AudioThreadBytes} B, worst callback {WorstCallbackFraction:P1}, "
        + $"{Overruns} over budget.";
}
