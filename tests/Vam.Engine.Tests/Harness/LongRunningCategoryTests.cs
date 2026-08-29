using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Harness;

/// <summary>
/// Proves the default run excludes <see cref="TestCategories.LongRunning"/>. If this ever
/// runs in a plain <c>dotnet test</c>, the gate has stopped working and the soaks are about
/// to start running in CI.
/// </summary>
public class LongRunningCategoryTests
{
    [Fact(
        Skip = "Long-running tests are excluded by default. Set VAM_LONGRUNNING=1 to run them.",
        SkipType = typeof(LongRunningTests),
        SkipUnless = nameof(LongRunningTests.IsEnabled))]
    [Trait("Category", TestCategories.LongRunning)]
    public void LongRunningTestsRunOnlyWhenAskedFor()
    {
        Assert.True(LongRunningTests.IsEnabled);
    }
}
