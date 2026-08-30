using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// One capture device, driven on its own thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Drives <c>IAudioCaptureClient</c> directly rather than using NAudio's <c>WasapiCapture</c>.</b>
/// That wrapper raises an event carrying a <c>WaveInEventArgs</c>, which is an object created on the
/// one thread whose timing the whole engine rests on. The boundary in <c>docs/audio-path.md</c> puts
/// our code inside the audio path from the moment we take the buffer, and an allocation per callback
/// is exactly what that rule exists to forbid.
/// </para>
/// <para>
/// The thread does one thing: wait for the device, convert the packet once, write it into whatever
/// the callback points at. No logging, no locks, no allocation. The only exception is the fault
/// path, where the thread is on its way out and the deadline it was protecting no longer exists.
/// </para>
/// </remarks>
public sealed class WasapiCaptureStream : ICaptureStream
{
    /// <summary>
    /// How long to wait for the device before deciding it has stopped talking to us. Generous
    /// against any sane buffer, and short enough that a dead device is noticed rather than hung on.
    /// </summary>
    static readonly TimeSpan DeviceTimeout = TimeSpan.FromSeconds(2);

    readonly MMDevice device;
    readonly AudioClient client;
    readonly AudioCaptureClient capture;
    readonly WasapiSampleReader reader;
    readonly ILogger logger;
    readonly EventWaitHandle packetReady = new(false, EventResetMode.AutoReset);
    readonly CancellationTokenSource stopping = new();

    CaptureCallback? onSamplesCaptured;
    Thread? worker;
    int state = (int)DeviceStreamState.Stopped;

    internal WasapiCaptureStream(
        AudioDeviceId deviceId,
        MMDevice device,
        AudioClient client,
        AudioStreamFormat format,
        WaveFormat granted,
        ILogger logger)
    {
        DeviceId = deviceId;
        Format = format;

        this.device = device;
        this.client = client;
        this.logger = logger;

        capture = client.AudioCaptureClient;
        reader = new WasapiSampleReader(granted, client.BufferSize);

        client.SetEventHandle(packetReady.SafeWaitHandle.DangerousGetHandle());
    }

    /// <inheritdoc />
    public AudioDeviceId DeviceId { get; }

    /// <inheritdoc />
    public DeviceDirection Direction => DeviceDirection.Capture;

    /// <inheritdoc />
    public AudioStreamFormat Format { get; }

    /// <inheritdoc />
    public DeviceStreamState State
    {
        get => (DeviceStreamState)Volatile.Read(ref state);
        private set => Volatile.Write(ref state, (int)value);
    }

    /// <summary>
    /// What killed the stream, when <see cref="State"/> is <see cref="DeviceStreamState.Faulted"/>.
    /// </summary>
    /// <remarks>
    /// Kept rather than rethrown. An exception cannot cross back into a callback, and a device that
    /// failed must take down its own strip and nothing else.
    /// </remarks>
    public Exception? Fault { get; private set; }

    /// <summary>Whether the thread got its multimedia scheduling. False means worse jitter, not failure.</summary>
    public bool IsProAudioScheduled { get; private set; }

    /// <summary>Packets delivered since the stream started.</summary>
    public long PacketsCaptured { get; private set; }

    /// <inheritdoc />
    public void Start(CaptureCallback onSamplesCaptured)
    {
        ArgumentNullException.ThrowIfNull(onSamplesCaptured);
        ObjectDisposedException.ThrowIf(stopping.IsCancellationRequested, this);

        if (State == DeviceStreamState.Running)
        {
            return;
        }

        // Stored once. Creating the delegate per callback would allocate inside the audio path,
        // which is the whole reason ICaptureStream takes it here rather than exposing an event.
        this.onSamplesCaptured = onSamplesCaptured;

        State = DeviceStreamState.Running;
        client.Start();

        worker = new Thread(Run)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = $"wasapi-capture-{DeviceId.Value}"
        };

        worker.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (worker is null)
        {
            return;
        }

        stopping.Cancel();
        packetReady.Set();

        // Joined rather than abandoned: the thread is holding a WASAPI buffer lease, and disposing
        // the client under it is how a clean shutdown becomes an access violation.
        worker.Join(DeviceTimeout);
        worker = null;

        TryStopClient();

        if (State == DeviceStreamState.Running)
        {
            State = DeviceStreamState.Stopped;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();

        stopping.Dispose();
        packetReady.Dispose();
        capture.Dispose();
        client.Dispose();
        device.Dispose();
    }

    void Run()
    {
        // Registered on this thread and released with it. Constructing it anywhere else would
        // schedule the wrong thread, which is a mistake that produces no error and no benefit.
        using ProAudioThread scheduling = new();

        IsProAudioScheduled = scheduling.IsRegistered;

        try
        {
            while (!stopping.IsCancellationRequested)
            {
                if (!packetReady.WaitOne(DeviceTimeout))
                {
                    continue;
                }

                DrainPackets();
            }
        }
        catch (Exception error)
        {
            // Never crosses back into the callback. The strip this device feeds gets muted off this
            // thread by the supervisor, and the rest of the session carries on without it.
            Fault = error;
            State = DeviceStreamState.Faulted;

            // The one logging call on this thread, and it is on the way out - the deadline this
            // thread existed to meet is already gone, and a fault nobody is told about is worse.
            logger.LogError(
                error,
                "Capture on {DeviceId} faulted after {PacketCount} packets. The strip is dead; the session is not.",
                DeviceId,
                PacketsCaptured);
        }
    }

    void DrainPackets()
    {
        int packetFrames = capture.GetNextPacketSize();

        while (packetFrames > 0 && !stopping.IsCancellationRequested)
        {
            nint buffer = capture.GetBuffer(out int frameCount, out AudioClientBufferFlags flags);

            try
            {
                if (frameCount > 0)
                {
                    bool isSilent = (flags & AudioClientBufferFlags.Silent) != 0;

                    onSamplesCaptured?.Invoke(reader.Read(buffer, frameCount, isSilent), frameCount);
                    PacketsCaptured++;
                }
            }
            finally
            {
                // Released even if the callback threw, or WASAPI's buffer stays leased and the
                // device stops delivering to everyone, this process included.
                capture.ReleaseBuffer(frameCount);
            }

            packetFrames = capture.GetNextPacketSize();
        }
    }

    void TryStopClient()
    {
        try
        {
            client.Stop();
        }
        catch (Exception error)
        {
            // A device unplugged mid-session throws here, and that is an event rather than a fault:
            // there is nothing left to stop. Recorded so a supervisor can tell the two apart.
            Fault ??= error;
            State = DeviceStreamState.Absent;
        }
    }
}
