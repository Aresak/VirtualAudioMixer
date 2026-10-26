namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// What a backend knows about a device before anything opens it.
/// </summary>
/// <param name="Id">Stable identity. This, not the name, is what gets persisted.</param>
/// <param name="FriendlyName">What the operating system calls it. For display only - it is not unique.</param>
/// <param name="Direction">Capture or render.</param>
/// <param name="ChannelCount">Channels the device offers.</param>
/// <param name="NominalSampleRate">
/// The rate the device presents to applications sharing it. What it actually runs at is measured
/// later, and what the hardware itself runs at is <paramref name="NativeSampleRate"/>.
/// </param>
/// <param name="SupportsExclusiveMode">
/// Whether the device will grant <see cref="ShareMode.Exclusive"/> at its own format. Shared mode
/// is always available; exclusive is the one a device can refuse, and refusing it changes the
/// latency budget.
/// <para>
/// Advisory. Whether exclusive mode is actually <i>taken</i> is decided at the open, and a device
/// that would grant it at a rate the engine cannot use is opened shared anyway.
/// </para>
/// </param>
/// <param name="IsVirtual">
/// Whether this endpoint comes from a virtual audio driver rather than hardware. Used to derive
/// mix-minus exclusion when the graph is built; nothing in the audio path ever branches on it.
/// </param>
/// <param name="ContainerId">
/// Which physical device this endpoint belongs to, or empty when the backend cannot say.
/// <para>
/// A headset or a speakerphone is one piece of hardware presenting two endpoints, and feeding its
/// microphone to its own speaker is the feedback loop mix-minus exists to prevent. Nothing else can
/// work that out: the two endpoints have different identities, and their names agree only by luck.
/// </para>
/// </param>
/// <param name="NativeSampleRate">
/// The rate the hardware itself runs at, or 0 when the backend cannot say.
/// <para>
/// This is the rate exclusive mode would deliver, and it is not always
/// <paramref name="NominalSampleRate"/>. A conference speakerphone can present a 48 kHz stereo mix
/// format while its own converter runs at 16 kHz mono, and shared mode hides the difference by
/// converting.
/// </para>
/// </param>
/// <param name="NativeChannelCount">Channels the hardware itself carries, or 0 when the backend cannot say.</param>
public sealed record AudioDeviceInfo(
    AudioDeviceId Id,
    string FriendlyName,
    DeviceDirection Direction,
    int ChannelCount,
    int NominalSampleRate,
    bool SupportsExclusiveMode = true,
    bool IsVirtual = false,
    string ContainerId = "",
    int NativeSampleRate = 0,
    int NativeChannelCount = 0);
