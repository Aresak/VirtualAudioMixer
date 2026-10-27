namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>Lists the processes running now.</summary>
public interface IProcessSnapshot
{
    /// <summary>Takes a snapshot.</summary>
    /// <returns>Every running process.</returns>
    IReadOnlyList<ProcessEntry> Take();
}
