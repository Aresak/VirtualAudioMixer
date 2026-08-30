namespace Vam.TestKit.Allocations;

/// <summary>
/// What a measured region allocated, and where it first did so.
/// </summary>
/// <param name="Bytes">Total managed bytes allocated across every measured iteration.</param>
/// <param name="Iterations">How many iterations were measured, excluding warm-up.</param>
/// <param name="FirstOffendingIteration">
/// The zero-based iteration that first allocated, or -1 when nothing did. Worth reporting
/// separately from the total: allocating only on iteration 0 usually means something was
/// still warming up, while allocating on every iteration is a real defect.
/// </param>
public readonly record struct AllocationMeasurement(long Bytes, int Iterations, int FirstOffendingIteration)
{
    /// <summary>Whether any managed allocation happened in the measured region.</summary>
    public bool HasAllocated => Bytes > 0;
}
