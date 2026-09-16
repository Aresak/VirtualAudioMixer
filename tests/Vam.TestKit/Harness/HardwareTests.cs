namespace Vam.TestKit.Harness;

/// <summary>
/// Whether tests that need real audio devices run in this process.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="LongRunningTests"/> because the two exclude for different reasons. A
/// soak is skipped because it is slow; a hardware test is skipped because the machine may have
/// nothing plugged into it, and on a CI runner it always does. Merging them would mean either
/// running soaks on every desktop or never running device tests anywhere.
/// </para>
/// <para>
/// An environment variable rather than a runner filter, for the same reason
/// <see cref="LongRunningTests"/> uses one: trait filtering does not survive a runner change, and a
/// gate that silently stops working is worse than no gate.
/// </para>
/// </remarks>
public static class HardwareTests
{
    /// <summary>Environment variable that opts a run into the tests needing real devices.</summary>
    public const string EnableVariable = "VAM_HARDWARE";

    /// <summary>
    /// True when <see cref="EnableVariable"/> is set to <c>1</c> or <c>true</c>.
    /// </summary>
    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable(EnableVariable) is "1" or "true" or "True" or "TRUE";
}
