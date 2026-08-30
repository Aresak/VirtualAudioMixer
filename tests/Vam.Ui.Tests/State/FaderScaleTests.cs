using Vam.TestKit.Harness;
using Vam.Ui.State;
using Xunit;

namespace Vam.Ui.Tests.State;

/// <summary>
/// The taper. A fader that does not put unity where the console draws it is a fader an operator
/// cannot trust, and everything else on the strip is built on top of the number it produces.
/// </summary>
public class FaderScaleTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void UnitySitsWhereTheConsoleDrawsIt()
    {
        // The mockup's unity line is at 28% from the top, which is 72% of the travel. If these ever
        // disagree, the console is drawing a line somewhere the fader does not pass through.
        Assert.Equal(0.0, FaderScale.ToDecibels(FaderScale.UnityPosition), 6);
        Assert.Equal(FaderScale.UnityPosition, FaderScale.ToPosition(0.0), 6);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheEndsAreTheEnds()
    {
        Assert.Equal(FaderScale.MinimumDb, FaderScale.ToDecibels(0.0), 6);
        Assert.Equal(FaderScale.MaximumDb, FaderScale.ToDecibels(1.0), 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.07)]
    [InlineData(0.15)]
    [InlineData(0.33)]
    [InlineData(0.4)]
    [InlineData(0.55)]
    [InlineData(0.72)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    [Trait("Category", TestCategories.Unit)]
    public void APositionSurvivesTheRoundTrip(double position)
    {
        // Both directions are used: the engine is sent decibels, and the control is drawn from what
        // comes back. A taper that is not its own inverse makes a fader crawl every time the console
        // refreshes.
        Assert.Equal(position, FaderScale.ToPosition(FaderScale.ToDecibels(position)), 6);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ItNeverGoesBackwards()
    {
        double previous = double.NegativeInfinity;

        for (int step = 0; step <= 1000; step++)
        {
            double decibels = FaderScale.ToDecibels(step / 1000.0);

            Assert.True(decibels >= previous, $"The taper doubled back at {step / 1000.0}.");
            previous = decibels;
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void MostOfTheTravelIsWhereTheAdjustmentsAre()
    {
        // Half the travel covers the twenty decibels below unity, which is the range somebody
        // actually works in during a meeting. A linear control would spend that half between minus
        // fifty and minus thirty, where everything is inaudible either way.
        double useful = FaderScale.ToPosition(0.0) - FaderScale.ToPosition(-20.0);

        Assert.True(useful > 0.3, $"Only {useful:P0} of the travel covers the top twenty decibels.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheBottomReadsAsOffRatherThanAsANumber()
    {
        // A fader at the bottom reads as silence. Minus one hundred is true and useless; the word is
        // what somebody glancing across sixteen strips can act on.
        Assert.Equal("−∞", FaderScale.Format(FaderScale.MinimumDb));
        Assert.Equal("+3.0", FaderScale.Format(3.0));
        Assert.Equal("-6.0", FaderScale.Format(-6.0));
    }
}
