using Vam.Core;

namespace Vam.TestKit.Allocations;

/// <summary>
/// Thrown when a region that must not allocate did.
/// </summary>
/// <remarks>
/// A dedicated exception rather than a test framework assertion, so <c>Vam.TestKit</c>
/// stays free of any test runner and the same check can run inside the soak driver.
/// </remarks>
public sealed class AllocationAssertException(string message, AllocationMeasurement measurement)
    : VamException(message)
{
    /// <summary>The measurement that failed.</summary>
    public AllocationMeasurement Measurement { get; } = measurement;
}
