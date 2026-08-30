using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// One render device, driven on its own thread, pulling audio rather than being pushed it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pull shape matters more than anything else here.</b> VAM-021 hangs the master clock on
/// exactly this: one designated render device's callback becomes the engine's timebase, and every
/// other device resamples onto it. A push model would have made that impossible to retrofit, which
/// is why the callback asks for frames instead of being handed them.
/// </para>
/// <para>
/// A slow fill delegate produces silence and a counter, never a stall. That is not a compromise —
/// blocking here to wait for audio that has not been mixed yet would turn one missing block into a
/// stopped device, and the second failure is much worse than the first.
/// </para>
/// </remarks>
public sealed class WasapiRenderStream : IRenderStream
{
    /// <summary>How long to wait for the device before deciding it has stopped asking for audio.</summary>
    static readonly TimeSpan DeviceTimeout = TimeSpan.FromSeconds(2);

    readonly MMDevice device;
    readonly AudioClient client;
    readonly AudioRenderClient render;
    readonly WasapiSampleWriter writer;
    readonly ILogger logger;
    readonly EventWaitHandle bufferReady = new(false, EventResetMode.AutoReset);
    readonly CancellationTokenSource stopping = new();
    readonly bool isExclusive;

    RenderCallback? onBufferNeeded;
    Thread? worker;
    int state = (int)DeviceStreamState.Stopped;
    long underrunCount;

    internal WasapiRenderStream(
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

        isExclusive = format.ShareMode == ShareMode.Exclusive;
        render = client.AudioRenderClient;
        writer = new WasapiSampleWriter(granted, client.BufferSize);

        client.SetEventHandle(bufferReady.SafeWaitHandle.DangerousGetHandle());
    }

    /// <inheritdoc />
    public AudioDeviceId DeviceId { get; }

    /// <inheritdoc />
    public DeviceDirection Direction => DeviceDirection.Render;

    /// <inheritdoc />
    public AudioStreamFormat Format { get; }

    /// <inheritdoc />
    public DeviceStreamState State
    {
        get => (DeviceStreamState)Volatile.Read(ref state);
        private set => Volatile.Write(ref state, (int)value);
    }

    /// <inheritdoc />
    public long UnderrunCount => Volatile.Read(ref underrunCount);

    /// <summary>What killed the stream, when <see cref="State"/> is <see cref="DeviceStreamState.Faulted"/>.</summary>
    public Exception? Fault { get; private set; }

    /// <summary>Whether the thread got its multimedia scheduling. False means worse jitter, not failure.</summary>
    public bool IsProAudioScheduled { get; private set; }

    /// <summary>Buffers filled since the stream started.</summary>
    public long BuffersRendered { get; private set; }

    /// <inheritdoc />
    public void Start(RenderCallback onBufferNeeded)
    {
        ArgumentNullException.ThrowIfNull(onBufferNeeded);
        ObjectDisposedException.ThrowIf(stopping.IsCancellationRequested, this);

        if (State == DeviceStreamState.Running)
        {
            return;
        }

        this.onBufferNeeded = onBufferNeeded;

        // Primed before the clock starts. WASAPI plays whatever is in its buffer the instant Start
        // returns, and an unprimed buffer is a click on the first block of every session.
        FillOneBuffer();

        State = DeviceStreamState.Running;
        client.Start();

        worker = new Thread(Run)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = $"wasapi-render-{DeviceId.Value}"
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
        bufferReady.Set();

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
        bufferReady.Dispose();
        render.Dispose();
        client.Dispose();
        device.Dispose();
    }

    void Run()
    {
        using ProAudioThread scheduling = new();

        IsProAudioScheduled = scheduling.IsRegistered;

        try
        {
            while (!stopping.IsCancellationRequested)
            {
                if (!bufferReady.WaitOne(DeviceTimeout))
                {
                    continue;
                }

                if (!stopping.IsCancellationRequested)
                {
                    FillOneBuffer();
                }
            }
        }
        catch (Exception error)
        {
            Fault = error;
            State = DeviceStreamState.Faulted;

            // The one logging call on this thread, and only as it dies. Nothing is playing out of
            // this device any more, so there is no deadline left to protect.
            logger.LogError(
                error,
                "Render on {DeviceId} faulted after {BufferCount} buffers. This output is dead; the session is not.",
                DeviceId,
                BuffersRendered);
        }
    }

    void FillOneBuffer()
    {
        int frameCount = FramesWanted();

        if (frameCount <= 0)
        {
            return;
        }

        nint buffer = render.GetBuffer(frameCount);
        int framesWritten = 0;

        try
        {
            framesWritten = onBufferNeeded?.Invoke(writer.Prepare(buffer, frameCount), frameCount) ?? 0;
            writer.Commit(buffer, frameCount, framesWritten);
        }
        finally
        {
            // Released even if the fill delegate threw, or WASAPI keeps the buffer leased and this
            // device stops playing for every application on the machine, not just this one.
            render.ReleaseBuffer(frameCount, AudioClientBufferFlags.None);
        }

        BuffersRendered++;

        if (framesWritten < frameCount)
        {
            underrunCount++;
        }
    }

    int FramesWanted()
    {
        // Exclusive mode hands the whole buffer over on every event; shared mode leaves whatever the
        // engine has not consumed yet, and asking for more than the free space is an error rather
        // than a truncation.
        return isExclusive ? client.BufferSize : client.BufferSize - client.CurrentPadding;
    }

    void TryStopClient()
    {
        try
        {
            client.Stop();
        }
        catch (Exception error)
        {
            Fault ??= error;
            State = DeviceStreamState.Absent;
        }
    }
}
