using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Harness;

/// <summary>
/// Proves the default run picks up <see cref="TestCategories.Unit"/>. Paired with
/// <see cref="LongRunningCategoryTests"/>, which the same run must skip.
/// </summary>
public class UnitCategoryTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void UnitTestsRunByDefault()
    {
        Assert.True(true);
    }
}
