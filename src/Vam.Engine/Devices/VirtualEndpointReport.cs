using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// What virtual endpoints a machine has, and what to say when it has none. A6 and E2.
/// </summary>
/// <remarks>
/// <para>
/// <b>The absent case is most of the work in this epic.</b> A first-time user without a virtual
/// driver should get a sentence telling them what is missing and where to get it, and everything
/// that does not need one should carry on working. A stack trace, or a session that refuses to
/// start, is the failure this type exists to prevent.
/// </para>
/// </remarks>
public sealed class VirtualEndpointReport
{
    VirtualEndpointReport(
        IReadOnlyList<AudioDeviceInfo> inputs,
        IReadOnlyList<AudioDeviceInfo> outputs,
        IReadOnlyList<VirtualDriver> drivers)
    {
        Inputs = inputs;
        Outputs = outputs;
        Drivers = drivers;
    }

    /// <summary>Virtual capture endpoints — where conferencing audio arrives. A6.</summary>
    public IReadOnlyList<AudioDeviceInfo> Inputs { get; }

    /// <summary>Virtual render endpoints — where a bus goes for OBS to capture it. E2.</summary>
    public IReadOnlyList<AudioDeviceInfo> Outputs { get; }

    /// <summary>Which drivers were recognised.</summary>
    public IReadOnlyList<VirtualDriver> Drivers { get; }

    /// <summary>Whether conferencing audio can reach VAM as a strip.</summary>
    public bool CanTakeConferencingAudio => Inputs.Count > 0;

    /// <summary>Whether a bus can reach OBS.</summary>
    public bool CanReachObs => Outputs.Count > 0;

    /// <summary>
    /// One sentence for the operator, whichever way it went.
    /// </summary>
    /// <remarks>
    /// Names what is missing and where to get it. "No virtual audio device found" tells somebody
    /// nothing they can act on, and this is the first thing a new user will read.
    /// </remarks>
    public string Description
    {
        get
        {
            if (Drivers.Count == 0)
            {
                return "No virtual audio driver is installed, so conferencing audio cannot come in and OBS "
                    + $"cannot capture the mix. Everything else works. Install one of: {InstallHints()}.";
            }

            string found = string.Join(", ", Drivers.Select(driver => driver.Name));

            if (CanTakeConferencingAudio && CanReachObs)
            {
                return $"Virtual endpoints are available through {found}.";
            }

            if (CanReachObs)
            {
                return $"{found} provides an output for OBS but no input, so conferencing audio cannot come in.";
            }

            return $"{found} provides an input but no output, so OBS cannot capture the mix.";
        }
    }

    /// <summary>Looks at what a backend reports and works out what is available.</summary>
    /// <param name="backend">The devices.</param>
    /// <returns>The report.</returns>
    public static VirtualEndpointReport From(IAudioBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);

        List<AudioDeviceInfo> inputs = [];
        List<AudioDeviceInfo> outputs = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        List<VirtualDriver> drivers = [];

        Collect(backend, DeviceDirection.Capture, inputs, names, drivers);
        Collect(backend, DeviceDirection.Render, outputs, names, drivers);

        return new VirtualEndpointReport(inputs, outputs, drivers);
    }

    static void Collect(
        IAudioBackend backend,
        DeviceDirection direction,
        List<AudioDeviceInfo> into,
        HashSet<string> seenDrivers,
        List<VirtualDriver> drivers)
    {
        foreach (AudioDeviceInfo device in backend.Enumerate(direction))
        {
            if (!device.IsVirtual)
            {
                continue;
            }

            into.Add(device);

            if (VirtualDriver.Recognise(device.FriendlyName) is { } driver && seenDrivers.Add(driver.Name))
            {
                drivers.Add(driver);
            }
        }
    }

    string InstallHints() =>
        string.Join(", ", VirtualDriver.Known.Select(driver => $"{driver.Name} ({driver.InstallHint})"));
}
