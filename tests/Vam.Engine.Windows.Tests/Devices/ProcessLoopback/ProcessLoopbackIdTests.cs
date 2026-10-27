using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.ProcessLoopback;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>
/// #55. How a <c>process:</c> identity is read, and which process it names.
/// </summary>
public class ProcessLoopbackIdTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANumberIsAProcessId()
    {
        Assert.True(ProcessLoopbackId.TryParse(new AudioDeviceId("process:4242"), out ProcessLoopbackId target));
        Assert.True(target.IsByProcessId);
        Assert.Equal(4242, target.ProcessId);
        Assert.Empty(target.ExecutableName);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData("process:msedge.exe", "msedge.exe")]
    [InlineData("process:msedge", "msedge.exe")]
    [InlineData("process:  chrome.EXE ", "chrome.EXE")]
    public void ANameIsAnExecutable(string value, string expected)
    {
        Assert.True(ProcessLoopbackId.TryParse(new AudioDeviceId(value), out ProcessLoopbackId target));
        Assert.False(target.IsByProcessId);
        Assert.Equal(expected, target.ExecutableName);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData("")]
    [InlineData("process:")]
    [InlineData("process:   ")]
    [InlineData("process:0")]
    [InlineData("process:99999999999")]
    [InlineData(@"process:C:\Program Files\Edge\msedge.exe")]
    [InlineData("process:a/b.exe")]
    [InlineData("Process:msedge.exe")]
    [InlineData("{0.0.1.00000000}.{221b0797-0000-0000-0000-000000000000}")]
    public void AnythingElseIsNotAProcessSource(string value)
    {
        Assert.False(ProcessLoopbackId.TryParse(new AudioDeviceId(value), out _));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnExecutableResolvesToTheRootOfItsTree()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(100, 1, "explorer.exe");
        processes.Start(200, 100, "msedge.exe");
        processes.Start(210, 200, "msedge.exe");
        processes.Start(220, 200, "msedge.exe");

        ProcessResolution resolution = Parse("process:MSEDGE").Resolve(processes.Take());

        // The audio service is a child. Capturing its tree alone would miss the rest of the browser.
        Assert.Equal(ProcessResolutionOutcome.Found, resolution.Outcome);
        Assert.Equal(200, resolution.Process.ProcessId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoInstancesOfOneExecutableAreRefusedRatherThanGuessed()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 100, "chrome.exe");
        processes.Start(210, 200, "chrome.exe");
        processes.Start(300, 100, "chrome.exe");

        ProcessResolution resolution = Parse("process:chrome.exe").Resolve(processes.Take());

        Assert.Equal(ProcessResolutionOutcome.Ambiguous, resolution.Outcome);
        Assert.Equal(2, resolution.Candidates);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AProcessIdResolvesOnlyWhileThatProcessRuns()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(4242, 1, "msedge.exe");
        ProcessLoopbackId target = Parse("process:4242");

        ProcessResolution running = target.Resolve(processes.Take());
        processes.Exit(4242);
        ProcessResolution closed = target.Resolve(processes.Take());

        Assert.Equal(ProcessResolutionOutcome.Found, running.Outcome);
        Assert.Equal("msedge.exe, PID 4242", target.Describe(running.Process));
        Assert.Equal(ProcessResolutionOutcome.Missing, closed.Outcome);
    }

    static ProcessLoopbackId Parse(string value)
    {
        Assert.True(ProcessLoopbackId.TryParse(new AudioDeviceId(value), out ProcessLoopbackId target));
        return target;
    }
}
