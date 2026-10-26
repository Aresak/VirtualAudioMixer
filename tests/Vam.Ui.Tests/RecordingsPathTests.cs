using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Extensions;
using Xunit;

namespace Vam.Ui.Tests;

/// <summary>What Apply on the recording folder box sends.</summary>
public class RecordingsPathTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheShownPathIsSentAndNeverAsAReset()
    {
        SetRecordingsPath? command = @"D:\Recordings ".ToRecordingsPath();

        Assert.NotNull(command);
        Assert.Equal(@"D:\Recordings", command.Path);
        Assert.False(command.Reset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", TestCategories.Unit)]
    public void AnEmptyBoxSendsNothing(string? shown) => Assert.Null(shown.ToRecordingsPath());
}
