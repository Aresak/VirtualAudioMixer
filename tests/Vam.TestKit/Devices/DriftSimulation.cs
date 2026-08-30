using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;

namespace Vam.TestKit.Devices;

/// <summary>
/// Runs a mix clock and a set of free-running capture devices against each other in simulated time.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of <see cref="NullAudioBackend"/> is that a device's real rate can be a figure
/// known exactly rather than one hoped for, and this is what turns that into a run: the mix side
/// pulls at exactly the nominal rate, each device delivers at its own, and the gap between them is
/// the drift the servo has to find. Eight hours of it takes a few minutes of CPU instead of an
/// evening in the room.
/// </para>
/// <para>
/// Stepped explicitly rather than driven by a clock, so no assertion here depends on how loaded the
/// machine is. A test that fails at random is worse than no test.
/// </para>
/// </remarks>
public sealed class DriftSimulation : IDisposable
{
    /// <summary>Capacity as a multiple of the setpoint, so a device may run well ahead before overrunning.</summary>
    const int RingCapacityMultiple = 4;

    readonly NullAudioBackend backend = new();
    readonly List<NullCaptureStream> streams = [];
    readonly List<DeviceInputChannel> channels = [];
    readonly List<bool> present = [];
    readonly DeviceInputChannelRegistry registry = new();
    readonly float[] block;
    readonly int blockFrames;
    readonly int targetFillFrames;
    readonly TimeSpan step;
    readonly TimeSpan correctionInterval;

    TimeSpan sinceCorrection;

    /// <summary>Sets up a run.</summary>
    /// <param name="blockFrames">Frames the mix clock pulls per block, and the device's buffer size.</param>
    /// <param name="targetFillFrames">Where each servo holds its ring.</param>
    /// <param name="correctionInterval">How often the corrections are advanced.</param>
    public DriftSimulation(int blockFrames, int targetFillFrames, TimeSpan correctionInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetFillFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(correctionInterval, TimeSpan.Zero);

        this.blockFrames = blockFrames;
        this.targetFillFrames = targetFillFrames;
        this.correctionInterval = correctionInterval;

        block = new float[blockFrames];
        step = TimeSpan.FromSeconds((double)blockFrames / NominalSampleRate);
    }

    /// <summary>The rate every device claims and the mix clock runs at.</summary>
    public static int NominalSampleRate => 48000;

    /// <summary>How much simulated time has passed.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>The channels under test, in the order they were added.</summary>
    public IReadOnlyList<DeviceInputChannel> Channels => channels;

    /// <summary>The registry holding them, for telemetry polling.</summary>
    public DeviceInputChannelRegistry Registry => registry;

    /// <summary>
    /// Adds a device whose clock sits <paramref name="driftPpm"/> away from nominal.
    /// </summary>
    /// <param name="friendlyName">Display name. Two devices may share one, deliberately.</param>
    /// <param name="driftPpm">How far its real clock runs from what it claims. Positive runs fast.</param>
    /// <returns>The channel carrying it.</returns>
    public DeviceInputChannel AddDevice(string friendlyName, double driftPpm)
    {
        AudioDeviceInfo info = backend.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions(
                friendlyName,
                ChannelCount: 1,
                NominalSampleRate: NominalSampleRate,
                DriftPpm: driftPpm,
                Signal: NullSignal.Tone));

        DeviceInputChannel channel = new(
            info.Id,
            new DeviceInputChannelOptions
            {
                NominalSampleRate = NominalSampleRate,
                ChannelCount = 1,
                BlockFrames = blockFrames,
                RingCapacityFrames = targetFillFrames * RingCapacityMultiple,
                TargetFillFrames = targetFillFrames,
                CorrectionInterval = correctionInterval
            },
            NullLogger<DeviceInputChannel>.Instance);

        ICaptureStream stream = backend.OpenCapture(
            info.Id,
            new CaptureOptions(ShareMode.Exclusive, step, ChannelCount: 1));

        stream.Start(channel.Write);
        channel.State = DeviceStreamState.Running;

        streams.Add((NullCaptureStream)stream);
        channels.Add(channel);
        present.Add(true);
        registry.Add(channel);

        return channel;
    }

    /// <summary>
    /// Fills every ring to its setpoint before the mix clock starts pulling.
    /// </summary>
    /// <remarks>
    /// A real session does this too - a ring that starts empty is a ring the servo spends its first
    /// minute climbing out of, and that startup transient would otherwise be measured as if it were
    /// drift.
    /// </remarks>
    public void Prime()
    {
        while (LowestFill() < targetFillFrames)
        {
            PumpDevices();
        }
    }

    /// <summary>Advances one block: every device delivers, then the mix clock pulls from every channel.</summary>
    public void Step()
    {
        PumpDevices();

        for (int index = 0; index < channels.Count; index++)
        {
            channels[index].Pull(block);
        }

        Elapsed += step;
        sinceCorrection += step;

        if (sinceCorrection >= correctionInterval)
        {
            registry.UpdateCorrections(sinceCorrection);
            sinceCorrection = TimeSpan.Zero;
        }
    }

    /// <summary>Runs until <paramref name="duration"/> of simulated time has passed.</summary>
    /// <param name="duration">How long to run for.</param>
    public void Run(TimeSpan duration)
    {
        TimeSpan until = Elapsed + duration;

        while (Elapsed < until)
        {
            Step();
        }
    }

    /// <summary>
    /// Takes a device away mid-run, the way an unplugged cable would. I5.
    /// </summary>
    /// <remarks>
    /// Injectable rather than only observable, because the supervisor's whole state machine is the
    /// part with the bugs in it and a soak that never loses a device never runs any of it.
    /// </remarks>
    /// <param name="index">Which device, in the order they were added.</param>
    public void RemoveDevice(int index)
    {
        present[index] = false;
        streams[index].SimulateRemoval();
        channels[index].State = DeviceStreamState.Absent;
    }

    /// <summary>
    /// Puts a device back, with its buffers and its drift estimate cleared as a real one would be.
    /// </summary>
    /// <param name="index">Which device.</param>
    public void RestoreDevice(int index)
    {
        channels[index].Reset();
        streams[index].Start(channels[index].Write);
        channels[index].State = DeviceStreamState.Running;
        present[index] = true;
    }

    /// <summary>Whether a device is currently plugged in, as far as this run is concerned.</summary>
    /// <param name="index">Which device.</param>
    /// <returns>Whether it is present.</returns>
    public bool IsPresent(int index) => present[index];

    /// <summary>The emptiest ring right now.</summary>
    /// <returns>Its fill in frames, or zero when there are no devices.</returns>
    public int LowestFill()
    {
        int lowest = int.MaxValue;

        for (int index = 0; index < channels.Count; index++)
        {
            lowest = Math.Min(lowest, channels[index].FillFrames);
        }

        return lowest == int.MaxValue ? 0 : lowest;
    }

    /// <summary>The real rate of the device behind a channel, which is what the estimate is judged against.</summary>
    /// <param name="index">Which channel, in the order they were added.</param>
    /// <returns>Frames per second.</returns>
    public double EffectiveSampleRateOf(int index) => streams[index].EffectiveSampleRate;

    /// <inheritdoc />
    public void Dispose() => backend.Dispose();

    void PumpDevices()
    {
        for (int index = 0; index < streams.Count; index++)
        {
            if (present[index])
            {
                streams[index].Pump(step);
            }
        }
    }
}
