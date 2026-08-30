using System.Diagnostics;
using Vam.Engine.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-010's stress criterion: a producer and a consumer at deliberately mismatched rates for ten
/// minutes, with no sample lost, duplicated or reordered.
/// </summary>
/// <remarks>
/// Ten minutes because the bug this is looking for does not appear in ten seconds. The rates are
/// mismatched so the ring spends the run swinging between full and empty rather than settling into
/// a comfortable middle where the wrap arithmetic is never stressed.
/// </remarks>
public class AudioRingBufferStressTests
{
    const int CapacityFrames = 512;
    const int ChannelCount = 2;
    const int ProducerBlockFrames = 120;

    // Deliberately not a divisor of the producer's block or the capacity, so the read offset lands
    // somewhere different on nearly every pass and the wrap is hit from every alignment.
    const int ConsumerBlockFrames = 77;

    // A float holds integers exactly only up to 2^24, and ten minutes at 48 kHz is far past that.
    // The ramp therefore repeats, which still catches a lost, duplicated or reordered frame.
    const long RampPeriod = 1 << 20;

    static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);

    [Fact(
        Skip = "Long-running tests are excluded by default. Set VAM_LONGRUNNING=1 to run them.",
        SkipType = typeof(LongRunningTests),
        SkipUnless = nameof(LongRunningTests.IsEnabled))]
    [Trait("Category", TestCategories.LongRunning)]
    public void TenMinutesAtMismatchedRatesLosesNothing()
    {
        AudioRingBuffer ring = new(CapacityFrames, ChannelCount);
        using CancellationTokenSource finished = new();

        string? failure = null;
        long framesProduced = 0;
        long framesConsumed = 0;

        Thread producer = new(() =>
        {
            float[] block = new float[ProducerBlockFrames * ChannelCount];
            long position = 0;

            while (!finished.IsCancellationRequested)
            {
                for (int frame = 0; frame < ProducerBlockFrames; frame++)
                {
                    float value = (position + frame) % RampPeriod;

                    for (int channel = 0; channel < ChannelCount; channel++)
                    {
                        block[(frame * ChannelCount) + channel] = value;
                    }
                }

                // On overrun the same block is retried rather than dropped, so the stream the
                // consumer sees must be perfectly continuous. Any gap at all is a defect.
                if (ring.TryWrite(block))
                {
                    position += ProducerBlockFrames;
                    Interlocked.Add(ref framesProduced, ProducerBlockFrames);
                }
                else
                {
                    Thread.SpinWait(40);
                }
            }
        })
        {
            IsBackground = true,
            Name = "ring-producer"
        };

        Thread consumer = new(() =>
        {
            float[] block = new float[ConsumerBlockFrames * ChannelCount];
            long position = 0;

            while (!finished.IsCancellationRequested)
            {
                int frames = ring.Read(block);

                if (frames == 0)
                {
                    Thread.SpinWait(60);
                    continue;
                }

                for (int frame = 0; frame < frames; frame++)
                {
                    float expected = (position + frame) % RampPeriod;

                    for (int channel = 0; channel < ChannelCount; channel++)
                    {
                        float actual = block[(frame * ChannelCount) + channel];

                        if (actual != expected)
                        {
                            failure =
                                $"At frame {position + frame} channel {channel}: expected {expected}, read {actual}. " +
                                $"Overruns {ring.OverrunCount}, underruns {ring.UnderrunCount}, fill {ring.FillFrames}.";
                            finished.Cancel();
                            return;
                        }
                    }
                }

                position += frames;
                Interlocked.Add(ref framesConsumed, frames);
            }
        })
        {
            IsBackground = true,
            Name = "ring-consumer"
        };

        producer.Start();
        consumer.Start();

        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < Duration && !finished.IsCancellationRequested)
        {
            Thread.Sleep(250);
        }

        finished.Cancel();
        producer.Join(TimeSpan.FromSeconds(10));
        consumer.Join(TimeSpan.FromSeconds(10));

        Assert.Null(failure);

        // Both sides must have been exercised, or the run proved nothing about the wrap.
        Assert.True(
            Interlocked.Read(ref framesConsumed) > CapacityFrames * 1000,
            $"Only {Interlocked.Read(ref framesConsumed)} frames were consumed; the run did not stress anything.");

        Assert.True(
            ring.OverrunCount > 0 && ring.UnderrunCount > 0,
            $"Expected the ring to hit both limits. Overruns {ring.OverrunCount}, underruns {ring.UnderrunCount}.");
    }
}
