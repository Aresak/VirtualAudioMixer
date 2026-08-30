namespace Vam.Engine.Devices.Abstractions;

/// <summary>How a stream shares its device with the rest of the machine.</summary>
public enum ShareMode
{
    /// <summary>
    /// The operating system mixes VAM with everything else. Higher latency, and the system may
    /// resample without saying so. The only option for a virtual endpoint, which another
    /// application has to keep using at the same time.
    /// </summary>
    Shared,

    /// <summary>
    /// VAM owns the device. Lower latency and no hidden resampling, at the cost of locking every
    /// other application out of it.
    /// </summary>
    Exclusive
}
