using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-013. The two-identical-Jabras case is the whole point of the task: it breaks identity by
/// name and identity by index at the same time.
/// </summary>
public class DeviceRegistryTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoDevicesOfTheSameModelResolveToDifferentIdentities()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo left = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Jabra Speak 750"));
        AudioDeviceInfo right = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Jabra Speak 750"));

        DeviceRegistry registry = new();
        registry.Refresh(backend);
        registry.Remember(left, stripIndex: 0);
        registry.Remember(right, stripIndex: 1);

        Assert.Equal(0, registry.Resolve(left.Id).StripIndex);
        Assert.Equal(1, registry.Resolve(right.Id).StripIndex);
        Assert.True(registry.Resolve(left.Id).IsPresent);
        Assert.True(registry.Resolve(right.Id).IsPresent);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void UnpluggingOneDeviceDoesNotMakeAnotherResolveAsIt()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo first = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Jabra Speak 750"));
        AudioDeviceInfo second = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Jabra Speak 750"));

        DeviceRegistry registry = new();
        registry.Refresh(backend);
        registry.Remember(first, stripIndex: 0);
        registry.Remember(second, stripIndex: 1);

        // With index-based identity, removing the first device would shuffle the second into its
        // place and strip 0 would silently start carrying the wrong microphone.
        backend.RemoveDevice(first.Id);
        registry.Refresh(backend);

        DeviceResolution gone = registry.Resolve(first.Id);
        DeviceResolution survivor = registry.Resolve(second.Id);

        Assert.Equal(DeviceAvailability.Absent, gone.Availability);
        Assert.True(survivor.IsPresent);
        Assert.Equal(1, survivor.StripIndex);
        Assert.Equal(second.Id, survivor.Device!.Id);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARememberedDeviceThatIsGoneYieldsAbsentWithItsLastKnownName()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Mayor 180"));

        DeviceRegistry registry = new();
        registry.Refresh(backend);
        registry.Remember(device, stripIndex: 2);

        backend.RemoveDevice(device.Id);
        registry.Refresh(backend);

        DeviceResolution resolution = registry.Resolve(device.Id);

        // Not an exception. The interface has to be able to say which device is missing.
        Assert.Equal(DeviceAvailability.Absent, resolution.Availability);
        Assert.Equal("Mayor 180", resolution.DisplayName);
        Assert.Equal(2, resolution.StripIndex);
        Assert.Null(resolution.Device);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnIdentityNeverSeenResolvesToUnknown()
    {
        DeviceRegistry registry = new();

        DeviceResolution resolution = registry.Resolve(new AudioDeviceId("null:Capture:999"));

        Assert.Equal(DeviceAvailability.Unknown, resolution.Availability);
        Assert.Equal(-1, resolution.StripIndex);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARenamedDeviceKeepsItsIdentityAndUpdatesItsName()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo original = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("USB Audio Device"));

        DeviceRegistry registry = new();
        registry.Refresh(backend);
        registry.Remember(original, stripIndex: 0);

        // A driver update renames the endpoint. Identity is unchanged, so the strip must survive it.
        backend.RemoveDevice(original.Id);
        AudioDeviceInfo renamed = original with { FriendlyName = "Behringer UCA222" };
        registry.Restore(new DeviceRegistrySnapshot([
            new RememberedDevice(renamed.Id, "USB Audio Device", DeviceDirection.Capture, 0)
        ]));

        Assert.Equal("USB Audio Device", registry.Resolve(renamed.Id).DisplayName);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheSnapshotRoundTripsAndSurvivesARestart()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo microphone = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Jabra Speak 750"));
        AudioDeviceInfo headphones = backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Realtek"));

        DeviceRegistry original = new();
        original.Refresh(backend);
        original.Remember(microphone, stripIndex: 1);
        original.Remember(headphones, stripIndex: 0);

        DeviceRegistrySnapshot snapshot = original.ToSnapshot();

        // Ordered by strip so a persisted file diffs sensibly instead of reshuffling every save.
        Assert.Equal([0, 1], snapshot.Devices.Select(device => device.StripIndex));

        DeviceRegistry restored = new();
        restored.Restore(snapshot);

        // Nothing has been enumerated yet, so everything is remembered but absent - which is
        // exactly the state VAM starts in before it has looked at the machine.
        Assert.Equal(DeviceAvailability.Absent, restored.Resolve(microphone.Id).Availability);
        Assert.Equal(1, restored.Resolve(microphone.Id).StripIndex);

        restored.Refresh(backend);
        Assert.True(restored.Resolve(microphone.Id).IsPresent);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ForgettingADeviceRemovesItFromTheMapping()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Audience"));

        DeviceRegistry registry = new();
        registry.Refresh(backend);
        registry.Remember(device, stripIndex: 3);

        Assert.True(registry.Forget(device.Id));
        Assert.False(registry.Forget(device.Id));

        // Still present on the machine, just no longer bound to a strip.
        Assert.True(registry.Resolve(device.Id).IsPresent);
        Assert.Equal(-1, registry.Resolve(device.Id).StripIndex);
    }
}
