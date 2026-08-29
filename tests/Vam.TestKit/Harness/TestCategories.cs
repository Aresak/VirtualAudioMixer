namespace Vam.TestKit.Harness;

/// <summary>
/// Values for the <c>Category</c> trait, which <c>.runsettings</c> filters on.
/// </summary>
/// <remarks>
/// Static because these are constants with no receiver to hang them on, the same carve-out the
/// style skill grants AllocationAssert. There is nothing here to mock.
/// </remarks>
public static class TestCategories
{
    /// <summary>Fast and deterministic. Runs on every build and in CI.</summary>
    public const string Unit = "unit";

    /// <summary>Soaks and stress runs, measured in minutes or hours. Excluded by default.</summary>
    public const string LongRunning = "longrunning";
}
