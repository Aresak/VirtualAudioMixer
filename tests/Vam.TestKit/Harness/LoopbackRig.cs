using Vam.Engine.Devices.Abstractions;

namespace Vam.TestKit.Harness;

/// <summary>
/// The two endpoints a loopback joins, named by environment variables.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="HardwareTests"/> because having devices and having them wired to each
/// other are different facts. Every machine that runs the hardware suite has a microphone; almost
/// none of them has a cable from an output back into an input, and a machine without one is not a
/// broken machine. So the loopback tests gate on both, and say which is missing.
/// </para>
/// <para>
/// Identities rather than names, for the reason <see cref="AudioDeviceId"/> gives: this room has two
/// identical Jabras, so a friendly name does not name a device.
/// </para>
/// <para>
/// Static for the same reason as its neighbours here - two environment variables have no receiver to
/// hang them on, and there is nothing to mock.
/// </para>
/// </remarks>
public static class LoopbackRig
{
    /// <summary>Environment variable naming the render endpoint the loopback is fed from.</summary>
    public const string RenderVariable = "VAM_LOOPBACK_OUT";

    /// <summary>Environment variable naming the capture endpoint the loopback arrives at.</summary>
    public const string CaptureVariable = "VAM_LOOPBACK_IN";

    /// <summary>The endpoint to play into, or <see cref="AudioDeviceId.None"/> when unset.</summary>
    public static AudioDeviceId RenderDeviceId => Read(RenderVariable);

    /// <summary>The endpoint to listen on, or <see cref="AudioDeviceId.None"/> when unset.</summary>
    public static AudioDeviceId CaptureDeviceId => Read(CaptureVariable);

    /// <summary>Whether both ends have been named.</summary>
    public static bool IsConfigured => !RenderDeviceId.IsNone && !CaptureDeviceId.IsNone;

    /// <summary>
    /// A sentence naming what is missing, for a test to skip with. Empty when nothing is.
    /// </summary>
    public static string MissingDescription
    {
        get
        {
            if (IsConfigured)
            {
                return string.Empty;
            }

            if (RenderDeviceId.IsNone && CaptureDeviceId.IsNone)
            {
                return $"No loopback is configured: set {RenderVariable} and {CaptureVariable} to the "
                    + "endpoint identities a cable or virtual cable joins.";
            }

            string unset = RenderDeviceId.IsNone ? RenderVariable : CaptureVariable;

            return $"Only one end of the loopback is named: {unset} is unset.";
        }
    }

    static AudioDeviceId Read(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);

        return string.IsNullOrWhiteSpace(value) ? AudioDeviceId.None : new AudioDeviceId(value.Trim());
    }
}
