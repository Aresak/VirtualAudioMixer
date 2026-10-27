using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Server.Engine;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>
/// #73. A strip given a device while the engine runs carries that device's audio without a restart.
/// </summary>
public sealed class RuntimeStripDeviceTests : IDisposable
{
    /// <summary>The RMS of the null devices' full-scale tone.</summary>
    const double ToneRmsDb = -3.01;

    readonly NullAudioBackend devices = new();
    readonly string workspace = Path.Combine(Path.GetTempPath(), "vam-runtime-" + Guid.NewGuid().ToString("n"));

    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ARetargetedStripCarriesItsNewDevice()
    {
        AudioDeviceInfo silent = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Lectern"));
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Monitor", ChannelCount: 2));

        using VamEngine engine = CreateEngine();
        using SignalPump pump = new(devices);
        engine.Start();

        AudioDeviceInfo tone = devices.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions("Mayor 180 degrees", Signal: NullSignal.Tone));

        Assert.True(await engine.OnControlLoopAsync(() => engine.RetargetChannel(0, tone.Id), Token));

        Assert.True(await HearsToneAsync(engine, 0), "The retargeted strip stayed silent.");

        AudioDeviceId[] tracked = await engine.OnControlLoopAsync(() => engine.Supervisor!.TrackedDevices.ToArray(), Token);

        Assert.DoesNotContain(silent.Id, tracked);
        Assert.Equal(1, engine.Channels.Count);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AStripAddedAtRuntimeCarriesItsDevice()
    {
        devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Lectern"));
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Monitor", ChannelCount: 2));

        using VamEngine engine = CreateEngine();
        using SignalPump pump = new(devices);
        engine.Start();

        AudioDeviceInfo tone = devices.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions("Mayor 180 degrees", Signal: NullSignal.Tone));

        int index = await engine.OnControlLoopAsync(
            () => engine.AddChannel(new ChannelConfig { DeviceId = tone.Id, Name = "Mayor", ChannelCount = 1 }),
            Token);

        Assert.Equal(1, index);
        Assert.True(await HearsToneAsync(engine, index), "The added strip stayed silent.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task TheRegistryAndTheInputOrderStayInStep()
    {
        AudioDeviceInfo first = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("A"));
        devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("B"));
        devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("C"));
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Monitor", ChannelCount: 2));

        using VamEngine engine = CreateEngine();
        engine.Start();

        AudioDeviceId fourth = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("D")).Id;
        AudioDeviceId fifth = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("E")).Id;
        AudioDeviceId sixth = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("F")).Id;
        AudioDeviceId seventh = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("G")).Id;
        AudioDeviceId eighth = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("H")).Id;

        // Strips 0-2 are A, B, C. Every kind of step: a new device, a shared one, a retarget away from
        // a shared device, one into a free slot, one onto a device already open, removals, and then a
        // new device by add and by retarget once removals have left slots vacant.
        (Func<bool> Step, AudioDeviceId FillsVacantSlot)[] steps =
        [
            (() => engine.AddChannel(new ChannelConfig { DeviceId = fourth, Name = "D" }) == 3, default),
            (() => engine.AddChannel(new ChannelConfig { DeviceId = first.Id, Name = "A again" }) == 4, default),
            (() => engine.RetargetChannel(0, fifth), default),
            (() => engine.RetargetChannel(1, sixth), default),
            (() => engine.RetargetChannel(2, first.Id), default),
            (() => engine.RemoveChannel(3), default),
            (() => engine.RemoveChannel(0), default),
            (() => engine.RetargetChannel(0, fourth), default),
            (() => engine.RemoveChannel(0), default),
            (() => engine.AddChannel(new ChannelConfig { DeviceId = seventh, Name = "G" }) == 2, seventh),
            (() => engine.RetargetChannel(0, eighth), eighth)
        ];

        AudioDeviceId[] before = await AssertInStepAsync(engine);

        foreach ((Func<bool> step, AudioDeviceId fillsVacantSlot) in steps)
        {
            Assert.True(await engine.OnControlLoopAsync(step, Token));

            AudioDeviceId[] after = await AssertInStepAsync(engine);

            if (!fillsVacantSlot.IsNone)
            {
                int slot = Array.IndexOf(after, fillsVacantSlot);

                Assert.Equal(before.Length, after.Length);
                Assert.Equal(DeviceSupervisor.VacantSlot, before[slot]);
                Assert.Equal(fillsVacantSlot, (await engine.OnControlLoopAsync(() => engine.Channels.Channels[slot], Token)).DeviceId);
            }

            // The clock pulls by slot under whichever plan is published, so a device that stays must
            // not move: a shift would hand a strip its neighbour's microphone until the recompile.
            for (int slot = 0; slot < before.Length; slot++)
            {
                if (before[slot] != DeviceSupervisor.VacantSlot && after.Contains(before[slot]))
                {
                    Assert.Equal(before[slot], after[slot]);
                }
            }

            before = after;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        devices.Dispose();

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    static async Task<AudioDeviceId[]> AssertInStepAsync(VamEngine engine)
    {
        (AudioDeviceId[] Registry, AudioDeviceId[] Order, AudioDeviceId[] Tracked, AudioDeviceId[] Strips) state =
            await engine.OnControlLoopAsync(
                () => (
                    engine.Channels.Channels.Select(engine.Supervisor!.DeviceInSlot).ToArray(),
                    engine.Graph!.Config.InputDeviceOrder.ToArray(),
                    engine.Supervisor!.TrackedDevices.ToArray(),
                    engine.Graph.Config.Channels.Select(channel => channel.DeviceId).ToArray()),
                Token);

        Assert.Equal(state.Registry, state.Order);

        // Nothing tracked that no strip uses, and nothing a strip uses left untracked.
        Assert.Equal(state.Strips.ToHashSet(), state.Tracked.ToHashSet());
        Assert.Equal(state.Tracked.ToHashSet(), state.Registry.Where(device => device != DeviceSupervisor.VacantSlot).ToHashSet());
        Assert.Equal(state.Tracked.Length, state.Registry.Count(device => device != DeviceSupervisor.VacantSlot));

        return state.Registry;
    }

    static async Task<bool> HearsToneAsync(VamEngine engine, int channelIndex)
    {
        Stopwatch waited = Stopwatch.StartNew();

        // Generous, because other suites share the machine.
        while (waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            double rmsDb = await engine.OnControlLoopAsync(
                () => engine.Meters is { } meters && channelIndex < meters.Channels.Length
                    ? meters.Channels[channelIndex].RmsDb
                    : double.NegativeInfinity,
                Token);

            if (Math.Abs(rmsDb - ToneRmsDb) < 0.5)
            {
                return true;
            }

            await Task.Delay(50, Token);
        }

        return false;
    }

    VamEngine CreateEngine() =>
        new(
            new EngineOptions
            {
                ConsolePath = Path.Combine(workspace, "console.json"),
                PresetPath = Path.Combine(workspace, "presets.json"),
                RecordingDirectory = Path.Combine(workspace, "recordings"),
                RecordAutomatically = false,
                ExpectedSessionDuration = TimeSpan.FromMinutes(1)
            },
            NullLoggerFactory.Instance,
            devices);
}
