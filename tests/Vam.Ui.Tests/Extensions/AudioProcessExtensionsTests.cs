using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Extensions;
using Xunit;

namespace Vam.Ui.Tests.Extensions;

/// <summary>The programs a strip's source selector offers beside the devices. #95.</summary>
public class AudioProcessExtensionsTests
{
    static readonly AudioProcess[] Programs =
    [
        new() { Id = "process:msedge.exe", Name = "msedge", IsPlaying = true },
        new() { Id = "process:Spotify.exe", Name = "Spotify" },
    ];

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AProgramAlreadyListedAsADeviceIsNotOfferedTwice()
    {
        DeviceInfo[] devices = [new() { Id = "process:MSEDGE.exe", Name = "msedge.exe" }];

        Assert.Equal(["process:Spotify.exe"], Programs.Besides(devices).Select(process => process.Id));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheSelectedProgramGivesItsName() => Assert.Equal("Spotify", Programs.NameOf("process:Spotify.exe"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("process:gone.exe")]
    [Trait("Category", TestCategories.Unit)]
    public void NoProgramGivesNoName(string? deviceId) => Assert.Equal(string.Empty, Programs.NameOf(deviceId));
}
