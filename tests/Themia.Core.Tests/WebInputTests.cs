using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The trust boundary in front of the network. A feed address, a ticker symbol and a place name all
/// reach the widget from a text box <i>and</i> from a JSON file anybody can hand-edit, so every
/// value here is one the app can genuinely be handed at startup without anyone having typed it.
/// </summary>
public class WebInputTests
{
    [Theory]
    [InlineData("https://example.com/feed.xml")]
    [InlineData("http://example.com/rss")]
    [InlineData("https://example.com:8443/feed?format=atom")]
    [InlineData("https://sub.domain.example.co.uk/a/b/c.atom")]
    public void A_plain_http_address_is_accepted(string url)
    {
        var result = WebInput.Url(url);

        Assert.True(result.IsValid);
        Assert.Equal(WebUrlProblem.None, result.Problem);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_because_that_is_what_a_paste_leaves()
    {
        var result = WebInput.Url("   https://example.com/feed.xml  ");

        Assert.True(result.IsValid);
        Assert.Equal("https://example.com/feed.xml", result.Url);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("FILE://localhost/etc/passwd")]
    public void A_file_url_is_refused(string url)
        => Assert.Equal(WebUrlProblem.UncPath, WebInput.Url(url).Problem);

    [Theory]
    [InlineData(@"\\server\share\feed.xml")]
    [InlineData("//server/share/feed.xml")]
    public void A_unc_path_is_refused(string url)
        => Assert.Equal(WebUrlProblem.UncPath, WebInput.Url(url).Problem);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/xml;base64,PD94bWw+")]
    [InlineData("ftp://example.com/feed.xml")]
    public void Only_http_and_https_are_allowed(string url)
        => Assert.Equal(WebUrlProblem.UnsupportedScheme, WebInput.Url(url).Problem);

    [Fact]
    public void Credentials_in_the_authority_are_refused_rather_than_stripped()
    {
        // Stripping them would change which resource is fetched without saying so, and keeping
        // them would put a password in the state file and in every log line naming the URL.
        var result = WebInput.Url("https://user:secret@example.com/feed.xml");

        Assert.False(result.IsValid);
        Assert.Equal(WebUrlProblem.Credentials, result.Problem);
    }

    [Theory]
    [InlineData("example.com/feed.xml")]
    [InlineData("/feed.xml")]
    [InlineData("just some words")]
    [InlineData("https://exa mple.com/feed")]
    public void A_relative_or_broken_address_is_refused_and_never_repaired(string url)
    {
        var result = WebInput.Url(url);

        Assert.False(result.IsValid);

        // Nothing is handed back. A caller that ignores IsValid gets an empty string rather than a
        // guess at what the user meant.
        Assert.Equal(string.Empty, result.Url);
    }

    [Fact]
    public void A_control_character_in_an_address_is_refused()
        => Assert.Equal(WebUrlProblem.Malformed, WebInput.Url("https://example.com/\u0000feed").Problem);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_address_is_empty_rather_than_malformed(string? url)
    {
        // Two different states for the widget: "you have not set this up" and "what you set up is
        // wrong" need different sentences.
        Assert.Equal(WebUrlProblem.Empty, WebInput.Url(url).Problem);
    }

    [Fact]
    public void An_absurdly_long_address_is_refused_before_it_is_parsed()
    {
        var url = "https://example.com/" + new string('a', WebInput.MaxUrlLength);

        Assert.Equal(WebUrlProblem.TooLong, WebInput.Url(url).Problem);
    }

    [Theory]
    [InlineData(WebUrlProblem.Malformed)]
    [InlineData(WebUrlProblem.UnsupportedScheme)]
    [InlineData(WebUrlProblem.UncPath)]
    [InlineData(WebUrlProblem.Credentials)]
    [InlineData(WebUrlProblem.TooLong)]
    [InlineData(WebUrlProblem.NoHost)]
    [InlineData(WebUrlProblem.Empty)]
    public void Every_refusal_has_something_to_say_to_the_user(WebUrlProblem problem)
        => Assert.NotEqual(string.Empty, WebInput.Explain(problem));

    // ---- symbols ---------------------------------------------------------------------------

    [Theory]
    [InlineData("AAPL")]
    [InlineData("^GSPC")]
    [InlineData("EURUSD=X")]
    [InlineData("GC=F")]
    [InlineData("BRK-B")]
    [InlineData("BMW.DE")]
    [InlineData("bitcoin")]
    [InlineData("ethereum-classic")]
    public void A_real_symbol_or_coin_id_is_accepted(string symbol)
        => Assert.True(WebInput.IsPlausibleSymbol(symbol));

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("AAPL/../../v7/finance/quote")]
    [InlineData("AAPL?range=1d&x=y")]
    [InlineData("AAPL AAPL")]
    [InlineData("AA%2FPL")]
    [InlineData("AAPL#frag")]
    [InlineData("^^^")]
    [InlineData("")]
    [InlineData(null)]
    public void A_symbol_that_could_carry_url_structure_is_refused(string? symbol)
    {
        // This is the check that matters most of the three: the symbol is interpolated into the
        // PATH of the quote endpoint, not into a query value.
        Assert.False(WebInput.IsPlausibleSymbol(symbol));
    }

    [Fact]
    public void A_symbol_longer_than_any_real_one_is_refused()
        => Assert.False(WebInput.IsPlausibleSymbol(new string('A', WebInput.MaxSymbolLength + 1)));

    [Fact]
    public void A_listed_symbol_is_upper_cased_and_a_coin_id_is_not()
    {
        Assert.Equal("AAPL", WebInput.NormaliseSymbol(" aapl ", crypto: false));
        Assert.Equal("bitcoin", WebInput.NormaliseSymbol(" BitCoin ", crypto: true));
    }

    // ---- places ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Zurich")]
    [InlineData("Sault Ste. Marie")]
    [InlineData("São Paulo")]
    [InlineData("東京")]
    public void A_place_name_is_accepted(string place)
        => Assert.True(WebInput.IsPlausiblePlace(place));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("!!!")]
    [InlineData(null)]
    public void Something_with_no_letters_in_it_is_not_a_place(string? place)
        => Assert.False(WebInput.IsPlausiblePlace(place));

    [Fact]
    public void A_place_with_a_control_character_is_refused()
        => Assert.False(WebInput.IsPlausiblePlace("Zurich\u0007"));

    // ---- coordinates -----------------------------------------------------------------------

    [Fact]
    public void Zero_is_a_real_coordinate()
    {
        // The Gulf of Guinea. A truthiness test on the numbers would read a widget pointed at the
        // equator as unconfigured, which is the bug the original app carries a spec bullet about.
        Assert.True(WebInput.AreCoordinates(0, 0));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(47.37, null)]
    [InlineData(null, 8.55)]
    [InlineData(91.0, 8.55)]
    [InlineData(47.37, 181.0)]
    [InlineData(double.NaN, 8.55)]
    [InlineData(47.37, double.PositiveInfinity)]
    public void A_missing_or_impossible_pair_is_not_a_location(double? latitude, double? longitude)
        => Assert.False(WebInput.AreCoordinates(latitude, longitude));
}
