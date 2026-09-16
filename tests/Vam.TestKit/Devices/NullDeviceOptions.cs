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
/// <param name="ContainerId">Which physical device this endpoint belongs to.</param>
/// <param name="NativeSampleRate">
/// The rate the hardware itself runs at, or 0 to say it is the nominal one.
/// <para>
/// Set it to something else to simulate a conference speakerphone: 16 kHz of its own behind a
/// 48 kHz mix format. That device is the reason exclusive mode is not simply taken wherever it is
/// offered, and it is the one case a test cannot reach without saying so here.
/// </para>
/// </param>
/// <param name="NativeChannelCount">Channels the hardware itself carries, or 0 for the nominal count.</param>
/// <param name="CanConvertRate">
/// Whether the backend can give the engine a rate the hardware does not run at. True for anything
/// with a system mixer behind it, which is what Windows shared mode is; false is what an ASIO
/// driver looks like, and the case where the engine has to refuse the device rather than open it.
/// </param>
public readonly record struct NullDeviceOptions(
    string FriendlyName,
    int ChannelCount = 1,
    int NominalSampleRate = 48000,
    double DriftPpm = 0.0,
    NullSignal Signal = NullSignal.Silence,
    double ToneFrequencyHz = 1000.0,
    bool SupportsExclusiveMode = true,
    bool IsVirtual = false,
    string ContainerId = "",
    int NativeSampleRate = 0,
    int NativeChannelCount = 0,
    bool CanConvertRate = true)
{
    /// <summary>The rate the hardware runs at, resolved against the nominal one.</summary>
    public int DeviceSampleRate => NativeSampleRate > 0 ? NativeSampleRate : NominalSampleRate;

    /// <summary>The width the hardware carries, resolved against the nominal one.</summary>
    public int DeviceChannelCount => NativeChannelCount > 0 ? NativeChannelCount : ChannelCount;
}
