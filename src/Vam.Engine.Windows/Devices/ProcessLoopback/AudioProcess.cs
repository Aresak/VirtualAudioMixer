using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>A running program with audio, offered as a strip's source.</summary>
/// <param name="DeviceId">The source, by executable, such as <c>process:msedge.exe</c>.</param>
/// <param name="Name">What the operator sees, such as <c>msedge</c>.</param>
/// <param name="IsPlaying">Whether any of its processes is playing now.</param>
public sealed record AudioProcess(AudioDeviceId DeviceId, string Name, bool IsPlaying);
