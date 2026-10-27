using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Extensions;
using Xunit;

namespace Vam.Ui.Tests;

/// <summary>The full device name a selector shows on hover.</summary>
public class DeviceNameTests
{
    static readonly DeviceInfo[] Devices =
    [
        new() { Id = "a", Name = "Microphone Array (Realtek(R) Audio) with a very long name" },
        new() { Id = "b", Name = "Jabra Speak 750" },
    ];

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheSelectedDeviceGivesItsFullName() =>
        Assert.Equal("Microphone Array (Realtek(R) Audio) with a very long name", Devices.NameOf("a"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gone")]
    [Trait("Category", TestCategories.Unit)]
    public void NoDeviceGivesNoName(string? deviceId) => Assert.Equal(string.Empty, Devices.NameOf(deviceId));
}
