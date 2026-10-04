using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The web element widget's rules. Most of these are about what comes back from the rendering
/// browser: third-party code ran there, so every reply is parsed into a typed value or refused.
/// </summary>
public class WebElementRulesTests
{
    [Theory]
    [InlineData(1, WebElementRules.MinRefreshMinutes)]
    [InlineData(-5, WebElementRules.MinRefreshMinutes)]
    [InlineData(99999, WebElementRules.MaxRefreshMinutes)]
    [InlineData(30, 30)]
    public void S475_the_refresh_is_clamped_into_range(int raw, int expected)
        => Assert.Equal(TimeSpan.FromMinutes(expected), new WebElementConfig { RefreshMinutes = raw }.RefreshInterval);

    [Fact]
    public void S475_the_default_refresh_is_fifteen_minutes()
        => Assert.Equal(TimeSpan.FromMinutes(15), new WebElementConfig().RefreshInterval);

    [Fact]
    public void S474_a_selector_is_trimmed()
        => Assert.Equal("#a > b", new WebElementConfig { Selector = "  #a > b  " }.EffectiveSelector);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("div\n> p")]
    [InlineData("div\u0000")]
    public void S474_an_implausible_selector_is_treated_as_unset(string selector)
        => Assert.Equal(string.Empty, new WebElementConfig { Selector = selector }.EffectiveSelector);

    [Fact]
    public void S474_an_overlong_selector_is_treated_as_unset()
        => Assert.Equal(string.Empty, new WebElementConfig { Selector = new string('a', WebElementRules.MaxSelectorLength + 1) }.EffectiveSelector);

    [Theory]
    [InlineData("https://www.example.com/page", "example.com")]
    [InlineData("https://news.example.com/", "news.example.com")]
    [InlineData("http://192.168.1.4:8080/status", "192.168.1.4")]
    public void S461_the_title_is_the_host_without_www(string url, string expected)
        => Assert.Equal(expected, WebElementRules.Title(url));

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    public void S461_an_unusable_address_gives_no_title(string url)
        => Assert.Equal(string.Empty, WebElementRules.Title(url));

    [Fact]
    public void S476_a_found_element_gives_its_box()
    {
        var probe = WebElementRules.ParseProbe("""{"found":true,"x":10.5,"y":200,"w":320,"h":48}""");

        Assert.Equal(ElementProbeStatus.Found, probe.Status);
        Assert.Equal(new ElementBox(10.5, 200, 320, 48), probe.Box);
    }

    [Fact]
    public void S477_a_selector_that_matched_nothing_is_not_found_not_an_error()
        => Assert.Equal(ElementProbeStatus.NotFound, WebElementRules.ParseProbe("""{"found":false}""").Status);

    [Fact]
    public void S477_a_matched_element_with_no_size_is_not_found()
        => Assert.Equal(ElementProbeStatus.NotFound, WebElementRules.ParseProbe("""{"found":true,"x":0,"y":0,"w":0,"h":20}""").Status);

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"found":"yes"}""")]
    [InlineData("""{"found":true,"x":1,"y":2,"w":3}""")]
    [InlineData("""{"found":true,"x":"1","y":2,"w":3,"h":4}""")]
    [InlineData("not json")]
    public void S478_a_reply_that_is_not_the_probe_shape_is_invalid(string json)
        => Assert.Equal(ElementProbeStatus.Invalid, WebElementRules.ParseProbe(json).Status);

    [Fact]
    public void S476_a_page_reporting_a_huge_or_negative_box_is_clamped()
    {
        var probe = WebElementRules.ParseProbe("""{"found":true,"x":-50,"y":-1,"w":1e9,"h":1e9}""");

        Assert.Equal(ElementProbeStatus.Found, probe.Status);
        Assert.Equal(0, probe.Box.X);
        Assert.Equal(0, probe.Box.Y);
        Assert.Equal(WebElementRules.MaxCaptureWidth, probe.Box.Width);
        Assert.Equal(WebElementRules.MaxCaptureHeight, probe.Box.Height);
    }

    [Fact]
    public void S478_the_probe_verdict_is_read_out_of_the_evaluate_reply()
        => Assert.Equal(
            """{"found":false}""",
            WebElementRules.ParseEvaluateString("""{"result":{"type":"string","value":"{\"found\":false}"}}"""));

    [Theory]
    [InlineData("""{"result":{"type":"object","value":{}}}""")]
    [InlineData("""{"result":{"type":"string","value":"x"},"exceptionDetails":{"text":"boom"}}""")]
    [InlineData("""{"result":{"type":"undefined"}}""")]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("nope")]
    public void S478_an_evaluate_reply_that_threw_or_returned_no_string_is_null(string json)
        => Assert.Null(WebElementRules.ParseEvaluateString(json));

    [Fact]
    public void S476_a_screenshot_reply_is_decoded_from_base64()
    {
        var png = WebElementRules.ParseScreenshot("""{"data":"iVBORw0KGgo="}""");

        Assert.NotNull(png);
        Assert.Equal(0x89, png![0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"data":""}""")]
    [InlineData("""{"data":"%%%not base64%%%"}""")]
    [InlineData("""{"data":42}""")]
    public void S476_a_screenshot_reply_without_a_picture_is_null(string json)
        => Assert.Null(WebElementRules.ParseScreenshot(json));

    [Fact]
    public void S479_a_pick_message_carries_the_page_the_user_was_on()
    {
        var message = WebElementRules.ParsePickerMessage(
            """{"kind":"themia-picked","selector":"#price","label":"  $9.99\n  each ","url":"https://shop.example.com/item?id=4"}""");

        Assert.Equal(PickerMessageKind.Picked, message.Kind);
        Assert.Equal("#price", message.Selector);
        Assert.Equal("$9.99 each", message.Label);
        Assert.Equal("https://shop.example.com/item?id=4", message.Url);
    }

    [Theory]
    [InlineData("""{"kind":"themia-picked","selector":"#a","url":"javascript:alert(1)"}""")]
    [InlineData("""{"kind":"themia-picked","selector":"#a","url":"file:///C:/x"}""")]
    [InlineData("""{"kind":"themia-picked","selector":"","url":"https://example.com"}""")]
    [InlineData("""{"kind":"themia-picked","url":"https://example.com"}""")]
    [InlineData("""{"kind":"themia-picked","selector":42,"url":"https://example.com"}""")]
    [InlineData("""{"kind":"install-everything"}""")]
    [InlineData("""{"selector":"#a","url":"https://example.com"}""")]
    [InlineData("\"themia-picked\"")]
    [InlineData("garbage")]
    public void S479_anything_but_the_two_known_shapes_is_ignored(string json)
        => Assert.Equal(PickerMessageKind.Invalid, WebElementRules.ParsePickerMessage(json).Kind);

    [Fact]
    public void S479_leaving_pick_mode_in_the_page_is_reported()
        => Assert.Equal(PickerMessageKind.Browsing, WebElementRules.ParsePickerMessage("""{"kind":"themia-browsing"}""").Kind);

    [Fact]
    public void S479_a_label_is_capped_to_one_short_line()
    {
        var label = WebElementRules.CleanLabel(new string('x', 500));
        Assert.Equal(WebElementRules.MaxLabelLength, label.Length);
    }
}
