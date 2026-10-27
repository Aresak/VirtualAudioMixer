namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>One audio session on an output, as Windows reports it.</summary>
/// <param name="ProcessId">The process that opened it.</param>
/// <param name="IsActive">Whether it is playing now, rather than open and quiet.</param>
public readonly record struct AudioSessionEntry(int ProcessId, bool IsActive);
