using Vam.TestKit.Harness;
using Vam.Ui.Extensions;
using Vam.Ui.Services;
using Xunit;

namespace Vam.Ui.Tests;

/// <summary>
/// What the console accepts when it asks somebody where the engine is.
/// </summary>
/// <remarks>
/// The box on the connection screen is the one place in the product where a person types something
/// that has to parse. Everything else they touch is a fader, a switch or a list. So this is where a
/// silent wrong answer would live: an address that parses into something plausible and wrong sends
/// the console somewhere it will wait forever, and the person is told only "nothing answered".
/// </remarks>
public class EngineAddressTests
{
    [Theory]
    [InlineData("192.168.1.50", "http://192.168.1.50:5211")]
    [InlineData("studio-pc", "http://studio-pc:5211")]
    [InlineData("localhost", "http://localhost:5211")]
    [Trait("Category", TestCategories.Unit)]
    public void ABareHostGetsTheSchemeAndThePort(string typed, string expected) =>
        Assert.Equal(expected, typed.ToEngineAddress());

    [Theory]
    [InlineData("192.168.1.50:5300", "http://192.168.1.50:5300")]
    [InlineData("http://studio-pc:5300", "http://studio-pc:5300")]
    [InlineData("https://studio-pc:5300", "https://studio-pc:5300")]
    [Trait("Category", TestCategories.Unit)]
    public void ATypedPortIsKept(string typed, string expected) =>
        Assert.Equal(expected, typed.ToEngineAddress());

    [Theory]
    [InlineData("http://studio-pc")]
    [InlineData("http://studio-pc:80")]
    [Trait("Category", TestCategories.Unit)]
    public void TheSchemesOwnPortIsNotAPortSomebodyMeant(string typed)
    {
        // :80 arrives either because it was typed - which nobody does meaning it - or because it was
        // never typed at all, which Uri reports the same way. An engine does not listen there, so
        // both become the engine's port rather than one of them becoming a dead address.
        Assert.Equal($"http://studio-pc:{VamSessionOptions.DefaultPort}", typed.ToEngineAddress());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", TestCategories.Unit)]
    public void NothingTypedIsNotAnAddress(string? typed) =>
        Assert.Null(typed.ToEngineAddress());

    [Theory]
    [InlineData("ftp://studio-pc")]
    [InlineData("file:///c:/engine")]
    [InlineData("://")]
    [Trait("Category", TestCategories.Unit)]
    public void SomethingThatIsNotAnHttpAddressIsRefused(string typed)
    {
        // Refused rather than coerced. gRPC would reject these later and less clearly, and a console
        // that quietly rewrote what somebody typed would be connecting somewhere they did not name.
        Assert.Null(typed.ToEngineAddress());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SurroundingSpaceIsIgnored()
    {
        // Pasted addresses carry it, and a person who pasted the right thing being told it is not an
        // address is the console being pedantic about something it can simply fix.
        Assert.Equal("http://192.168.1.50:5211", "  192.168.1.50 ".ToEngineAddress());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheDefaultLocalAddressSurvivesARoundTrip()
    {
        // The one that matters most: what the console assumes about an engine on this machine has to
        // still mean itself after going through the parser a remembered value comes back through.
        Assert.Equal(VamSessionOptions.LocalAddress, VamSessionOptions.LocalAddress.ToEngineAddress());
    }
}
