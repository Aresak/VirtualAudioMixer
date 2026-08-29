using System.Globalization;

namespace Vam.TestKit.Allocations;

/// <summary>
/// Asserts that a region of code allocates no managed memory.
/// </summary>
/// <remarks>
/// <para>
/// This is the enforcement behind the project's first rule. A garbage collection pause during
/// a three-hour broadcast is a dropout, and the only defence is that the audio path never
/// allocates. See <c>docs/audio-path.md</c> for where that boundary is drawn.
/// </para>
/// <para>
/// Static because there is nothing here to mock: it reads a runtime counter and compares
/// two numbers.
/// </para>
/// <para>
/// Prefer the closure-free <see cref="None{TState}(TState, Action{TState}, int, int)"/> overload
/// everywhere. A capturing lambda allocates its closure once, which is invisible in the total
/// but makes the harness measure itself the moment anything about the call changes.
/// </para>
/// </remarks>
public static class AllocationAssert
{
    const string Rule = "Nothing in the audio path allocates, locks or waits.";
    const string Boundary = "See docs/audio-path.md for where that boundary is drawn.";

    /// <summary>
    /// Runs <paramref name="body"/> and throws if it allocated.
    /// </summary>
    /// <param name="body">The region under test. Must not capture, or it measures its own closure.</param>
    /// <param name="warmup">
    /// Iterations run before measuring starts. First-call JIT, static constructors and tiered
    /// compilation all allocate, so without a warm-up every first measurement is a false positive.
    /// </param>
    /// <param name="iterations">Iterations measured.</param>
    /// <exception cref="AllocationAssertException">The region allocated.</exception>
    public static void None(Action body, int warmup = 16, int iterations = 64)
    {
        ArgumentNullException.ThrowIfNull(body);

        None(body, static state => state(), warmup, iterations);
    }

    /// <summary>
    /// Runs <paramref name="body"/> against <paramref name="state"/> and throws if it allocated.
    /// </summary>
    /// <typeparam name="TState">State passed to the body instead of being captured.</typeparam>
    /// <param name="state">Everything the body needs, so the delegate can stay static.</param>
    /// <param name="body">The region under test.</param>
    /// <param name="warmup">Iterations run before measuring starts.</param>
    /// <param name="iterations">Iterations measured.</param>
    /// <exception cref="AllocationAssertException">The region allocated.</exception>
    public static void None<TState>(TState state, Action<TState> body, int warmup = 16, int iterations = 64)
    {
        AllocationMeasurement measurement = Measure(state, body, warmup, iterations);

        if (measurement.HasAllocated)
        {
            throw new AllocationAssertException(Describe(measurement), measurement);
        }
    }

    /// <summary>
    /// Measures what <paramref name="body"/> allocates, without asserting anything.
    /// </summary>
    /// <param name="body">The region to measure. Must not capture.</param>
    /// <param name="warmup">Iterations run before measuring starts.</param>
    /// <param name="iterations">Iterations measured.</param>
    /// <returns>The measurement, whose <see cref="AllocationMeasurement.Bytes"/> may be zero.</returns>
    public static AllocationMeasurement Measure(Action body, int warmup = 16, int iterations = 64)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Measure(body, static state => state(), warmup, iterations);
    }

    /// <summary>
    /// Measures what <paramref name="body"/> allocates against <paramref name="state"/>.
    /// </summary>
    /// <typeparam name="TState">State passed to the body instead of being captured.</typeparam>
    /// <param name="state">Everything the body needs, so the delegate can stay static.</param>
    /// <param name="body">The region to measure.</param>
    /// <param name="warmup">Iterations run before measuring starts.</param>
    /// <param name="iterations">Iterations measured.</param>
    /// <returns>The measurement, whose <see cref="AllocationMeasurement.Bytes"/> may be zero.</returns>
    public static AllocationMeasurement Measure<TState>(
        TState state,
        Action<TState> body,
        int warmup = 16,
        int iterations = 64)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        for (int iteration = 0; iteration < warmup; iteration++)
        {
            body(state);
        }

        long totalBytes = 0;
        int firstOffendingIteration = -1;

        // Nothing between the two counter reads may allocate, including this loop's own
        // bookkeeping - that is why the delegate is generic over TState rather than captured.
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            body(state);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            if (allocated > 0 && firstOffendingIteration < 0)
            {
                firstOffendingIteration = iteration;
            }

            totalBytes += allocated;
        }

        return new AllocationMeasurement(totalBytes, iterations, firstOffendingIteration);
    }

    static string Describe(AllocationMeasurement measurement)
    {
        string plural = measurement.Bytes == 1 ? "byte" : "bytes";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
             Expected no allocation, but {measurement.Bytes} {plural} were allocated across {measurement.Iterations} iterations, first on iteration {measurement.FirstOffendingIteration}.
             {Rule}
             {Boundary}
             """);
    }
}
