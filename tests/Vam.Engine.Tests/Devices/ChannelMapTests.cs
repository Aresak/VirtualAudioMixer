using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Devices.Extensions;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-014. What makes a stereo interface into two independent mono strips, and what stops a
/// mapping mistake from reaching the audio path.
/// </summary>
public class ChannelMapTests
{
    const int Frames = 8;

    static readonly AudioDeviceId StereoId = new("null:stereo");
    static readonly AudioDeviceId MonoId = new("null:mono");

    static readonly AudioDeviceInfo Stereo =
        new(StereoId, "Stereo interface", DeviceDirection.Capture, 2, 48000);

    static readonly AudioDeviceInfo Mono =
        new(MonoId, "Mayor 180 degrees", DeviceDirection.Capture, 1, 48000);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStereoDeviceBecomesTwoStripsCarryingDifferentAudio()
    {
        ChannelSource left = new(StereoId, FirstChannel: 0, ChannelCount: 1, StripIndex: 0);
        ChannelSource right = new(StereoId, FirstChannel: 1, ChannelCount: 1, StripIndex: 1);

        // Left counts up, right counts down. Anything that crosses the two shows immediately.
        float[] interleaved = new float[Frames * 2];

        for (int frame = 0; frame < Frames; frame++)
        {
            interleaved[frame * 2] = frame;
            interleaved[(frame * 2) + 1] = -frame;
        }

        float[] toLeftStrip = new float[Frames];
        float[] toRightStrip = new float[Frames];

        ((ReadOnlySpan<float>)interleaved).ExtractInto(left, Stereo.ChannelCount, toLeftStrip);
        ((ReadOnlySpan<float>)interleaved).ExtractInto(right, Stereo.ChannelCount, toRightStrip);

        for (int frame = 0; frame < Frames; frame++)
        {
            Assert.Equal(frame, toLeftStrip[frame]);
            Assert.Equal(-frame, toRightStrip[frame]);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStereoPairCanStayOneStrip()
    {
        ChannelSource pair = new(StereoId, FirstChannel: 0, ChannelCount: 2, StripIndex: 0);

        float[] interleaved = [1, 2, 3, 4, 5, 6];
        float[] destination = new float[6];

        ((ReadOnlySpan<float>)interleaved).ExtractInto(pair, Stereo.ChannelCount, destination);

        // Still interleaved, and still in order. A pair taken whole is the same idea as a single
        // channel with a different length, which is why there is no branch on which it was.
        Assert.Equal(interleaved, destination);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void UnmappedChannelsAreNeverRead()
    {
        // A four-channel device with only its third channel wanted.
        ChannelSource third = new(StereoId, FirstChannel: 2, ChannelCount: 1, StripIndex: 0);

        float[] interleaved = [10, 11, 12, 13, 20, 21, 22, 23];
        float[] destination = new float[2];

        ((ReadOnlySpan<float>)interleaved).ExtractInto(third, deviceChannelCount: 4, destination);

        Assert.Equal(12, destination[0]);
        Assert.Equal(22, destination[1]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void DeInterleavingAllocatesNothing()
    {
        ChannelSource right = new(StereoId, FirstChannel: 1, ChannelCount: 1, StripIndex: 1);

        // The source travels in the state rather than being captured: a static lambda cannot reach
        // a local, and a capturing one would allocate the closure the harness is trying to measure.
        (float[] Interleaved, float[] Destination, ChannelSource Source) state =
            (new float[Frames * 2], new float[Frames], right);

        AllocationAssert.None(
            state,
            static work => ((ReadOnlySpan<float>)work.Interleaved).ExtractInto(work.Source, 2, work.Destination));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AValidMapReportsNothing()
    {
        ChannelMap map = new();

        map.Add(new ChannelSource(StereoId, 0, 1, 0));
        map.Add(new ChannelSource(StereoId, 1, 1, 1));
        map.Add(new ChannelSource(MonoId, 0, 1, 2));

        Assert.Empty(map.Validate([Stereo, Mono]));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnOutOfRangeChannelIsRefusedAndNamesTheDeviceAndChannel()
    {
        ChannelMap map = new();

        map.Add(new ChannelSource(MonoId, FirstChannel: 1, ChannelCount: 1, StripIndex: 0));

        ChannelMapProblem problem = Assert.Single(map.Validate([Mono]));

        Assert.Equal(ChannelMapProblemKind.ChannelOutOfRange, problem.Kind);

        // Named, because "channel out of range" on its own leaves the operator hunting for which.
        Assert.Contains("Mayor 180 degrees", problem.Description, StringComparison.Ordinal);
        Assert.Contains("offers 1", problem.Description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnAbsentDeviceAndADoubleClaimAreBothReported()
    {
        ChannelMap map = new();

        map.Add(new ChannelSource(StereoId, 0, 1, 0));
        map.Add(new ChannelSource(MonoId, 0, 1, 0));

        IReadOnlyList<ChannelMapProblem> problems = map.Validate([Stereo]);

        // Every problem at once. An operator fixing a console at five to seven should not discover
        // the next fault after each repair.
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.Kind == ChannelMapProblemKind.DeviceAbsent);
        Assert.Contains(problems, problem => problem.Kind == ChannelMapProblemKind.StripClaimedTwice);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMalformedSourceIsRefusedBeforeItReachesTheDeviceCheck()
    {
        ChannelMap map = new();

        map.Add(new ChannelSource(MonoId, FirstChannel: 0, ChannelCount: 0, StripIndex: 0));

        Assert.Contains(map.Validate([Mono]), problem => problem.Kind == ChannelMapProblemKind.MalformedSource);
    }
}
