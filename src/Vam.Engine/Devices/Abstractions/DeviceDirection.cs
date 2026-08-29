namespace Vam.Engine.Devices.Abstractions;

/// <summary>Which way audio moves through a device.</summary>
public enum DeviceDirection
{
    /// <summary>The device produces audio: a microphone, a line input, a virtual recording endpoint.</summary>
    Capture,

    /// <summary>The device consumes audio: headphones, a line output, a virtual playback endpoint.</summary>
    Render
}
