using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Allocations;

/// <summary>
/// Proves the allocation gate actually fires.
/// </summary>
/// <remarks>
/// The epic is not done when the utility exists, it is done when a violating change fails
/// the build. Those are different claims and only the second one is worth anything.
/// </remarks>
public class AllocationGateProofTests
{
    static object? sink;

    // This test allocates on purpose. It is not a defect and it must not be "cleaned up":
    // it is the only thing standing between AllocationAssert.None being real and it being a
    // no-op that everybody trusts. Replace the body of None with an empty method and this
    // test goes red - that is the point of it.
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void DeliberateAllocationInsideTheGateThrows()
    {
        Assert.Throws<AllocationAssertException>(
            () => AllocationAssert.None(8, static size => sink = new byte[size]));
    }
}
