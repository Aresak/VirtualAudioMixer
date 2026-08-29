namespace Vam.TestKit.Harness;

/// <summary>
/// Whether long-running tests run in this process.
/// </summary>
/// <remarks>
/// <para>
/// Soaks and stress runs are excluded by default, so a local <c>dotnet test</c> behaves
/// exactly the way CI does. Opting in is an environment variable rather than a runner
/// filter: trait filtering is a moving target across runners, and a gate that depends on
/// one is a gate that silently stops working.
/// </para>
/// <para>
/// Static because this is a constant and a single environment read, with no receiver and
/// nothing to mock.
/// </para>
/// </remarks>
public static class LongRunningTests
{
    /// <summary>Environment variable that opts a run into the long-running tests.</summary>
    public const string EnableVariable = "VAM_LONGRUNNING";

    /// <summary>
    /// True when <see cref="EnableVariable"/> is set to <c>1</c> or <c>true</c>.
    /// </summary>
    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable(EnableVariable) is "1" or "true" or "True" or "TRUE";
}
