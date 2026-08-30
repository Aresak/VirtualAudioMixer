using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// EPIC-13. Conferencing audio in, OBS out, and what happens on a machine with neither.
/// </summary>
public class VirtualEndpointTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMachineWithNoDriverIsToldWhatIsMissingAndWhereToGetIt()
    {
        using NullAudioBackend backend = new();

        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Speakers"));

        VirtualEndpointReport report = VirtualEndpointReport.From(backend);

        Assert.False(report.CanTakeConferencingAudio);
        Assert.False(report.CanReachObs);

        // A sentence, not a stack trace, and it names something the person can go and install.
        Assert.Contains("Everything else works", report.Description, StringComparison.Ordinal);
        Assert.Contains("vb-audio.com", report.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARecognisedDriverGivesBothDirections()
    {
        using NullAudioBackend backend = new();

        backend.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions("CABLE Output (VB-Audio Virtual Cable)", IsVirtual: true)
        );
        backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions("CABLE Input (VB-Audio Virtual Cable)", IsVirtual: true));

        VirtualEndpointReport report = VirtualEndpointReport.From(backend);

        Assert.True(report.CanTakeConferencingAudio);
        Assert.True(report.CanReachObs);
        Assert.Contains(report.Drivers, driver => driver.Name.Contains("Virtual Cable", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void HalfADriverIsReportedAsHalfADriver()
    {
        using NullAudioBackend backend = new();

        backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions("CABLE Input (VB-Audio Virtual Cable)", IsVirtual: true));

        VirtualEndpointReport report = VirtualEndpointReport.From(backend);

        Assert.False(report.CanTakeConferencingAudio);
        Assert.True(report.CanReachObs);

        // Names which half is missing. "Virtual endpoints unavailable" would leave an operator
        // wondering which of the two things they were trying to do had failed.
        Assert.Contains("conferencing audio cannot come in", report.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)", true)]
    [InlineData("VoiceMeeter Output (VB-Audio VoiceMeeter VAIO)", true)]
    [InlineData("Virtual Audio Driver (Speakers)", true)]
    [InlineData("Microphone (Trust USB microphone)", false)]
    [InlineData("Echo Cancelling Speakerphone (Jabra SPEAK 510 USB)", false)]
    [Trait("Category", TestCategories.Unit)]
    public void RealHardwareIsNotMistakenForADriver(string friendlyName, bool expected)
    {
        // Recognition, not identity. Getting it wrong costs a mislabelled strip rather than a
        // broken routing, which is why matching on a name is acceptable here and nowhere else.
        Assert.Equal(expected, VirtualDriver.Recognise(friendlyName) is not null);
    }
}
