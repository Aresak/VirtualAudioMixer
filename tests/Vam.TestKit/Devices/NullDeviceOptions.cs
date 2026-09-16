namespace Vam.TestKit.Devices;

/// <summary>
/// How one <see cref="NullAudioBackend"/> device behaves.
/// </summary>
/// <param name="FriendlyName">Display name. Deliberately allowed to collide with another device's.</param>
/// <param name="ChannelCount">Channels the device offers.</param>
/// <param name="NominalSampleRate">The rate the device claims, which is what it reports and persists.</param>
/// <param name="DriftPpm">
/// How far the device's real clock sits from its nominal rate, in parts per million. This is the
/// whole point of the null backend: a free-running USB device is never exactly 48 kHz, and a drift
/// bug does not appear in five minutes - it appears in hour three, as a click. Positive runs fast.
/// </param>
/// <param name="Signal">What a capture device produces. Ignored for render devices.</param>
/// <param name="ToneFrequencyHz">Frequency for <see cref="NullSignal.Tone"/>.</param>
/// <param name="SupportsExclusiveMode">Whether the device will grant exclusive mode.</param>
/// <param name="IsVirtual">Whether to present the device as a virtual endpoint.</param>
public readonly record struct NullDeviceOptions(
    string FriendlyName,
    int ChannelCount = 1,
    int NominalSampleRate = 48000,
    double DriftPpm = 0.0,
    NullSignal Signal = NullSignal.Silence,
    double ToneFrequencyHz = 1000.0,
    bool SupportsExclusiveMode = true,
    bool IsVirtual = false,
    string ContainerId = "");
