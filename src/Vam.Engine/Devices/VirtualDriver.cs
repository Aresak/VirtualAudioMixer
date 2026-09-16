namespace Vam.Engine.Devices;

/// <summary>
/// A virtual audio driver VAM knows how to recognise.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recognised, never required.</b> Which driver to depend on is still an open question — VB-Cable,
/// the MIT Virtual-Audio-Driver, or a fork of SAR — and hard-coding one now would settle a decision
/// that is currently being made on incomplete information. So VAM detects what is present and works
/// with whichever it finds.
/// </para>
/// <para>
/// The matching is on the endpoint's own name because that is what a driver actually controls. It is
/// not identity — identity is the endpoint identifier, as everywhere else — it is recognition, and
/// getting it wrong costs a mislabelled strip rather than a broken routing.
/// </para>
/// </remarks>
/// <param name="Name">What to call it when telling somebody it is missing.</param>
/// <param name="Marker">Text that appears in the friendly name of every endpoint it provides.</param>
/// <param name="InstallHint">Where to get it. Shown to a first-time user rather than a stack trace.</param>
public readonly record struct VirtualDriver(string Name, string Marker, string InstallHint)
{
    /// <summary>The drivers VAM recognises, in no particular order of preference.</summary>
    /// <remarks>
    /// No preference is expressed on purpose. Whichever one an operator has already installed is the
    /// one that works, and ranking them would be VAM having an opinion about somebody else's machine.
    /// </remarks>
    public static IReadOnlyList<VirtualDriver> Known { get; } =
    [
        new("VB-Audio Virtual Cable", "CABLE", "https://vb-audio.com/Cable/"),
        new("VB-Audio Voicemeeter", "VoiceMeeter", "https://vb-audio.com/Voicemeeter/"),
        new("Virtual Audio Driver", "Virtual Audio Driver", "https://github.com/VirtualDrivers/Virtual-Audio-Driver"),
        new("Synchronous Audio Router", "SAR", "https://github.com/eiz/SynchronousAudioRouter")
    ];

    /// <summary>Whether an endpoint's name looks like it came from this driver.</summary>
    /// <param name="friendlyName">What the operating system calls the endpoint.</param>
    /// <returns>Whether it matches.</returns>
    public bool Matches(string friendlyName) =>
        !string.IsNullOrEmpty(friendlyName)
        && friendlyName.Contains(Marker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Which known driver an endpoint appears to belong to.</summary>
    /// <param name="friendlyName">What the operating system calls it.</param>
    /// <returns>The driver, or null when it looks like real hardware.</returns>
    public static VirtualDriver? Recognise(string friendlyName)
    {
        foreach (VirtualDriver driver in Known)
        {
            if (driver.Matches(friendlyName))
            {
                return driver;
            }
        }

        return null;
    }
}
