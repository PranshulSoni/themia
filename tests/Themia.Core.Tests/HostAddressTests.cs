using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The ping host is a trust boundary: it is typed by a user, persisted, and later handed to a name
/// resolver, so both the settings box and the probe check it here.
/// </summary>
public class HostAddressTests
{
    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("example.com")]
    [InlineData("a.very-long.sub.domain.example.co.uk")]
    [InlineData("localhost")]
    [InlineData("my_server")]
    [InlineData("router.local.")]
    [InlineData("::1")]
    [InlineData("2001:4860:4860::8888")]
    public void Accepts_names_and_addresses(string host)
    {
        Assert.True(HostAddress.IsPlausible(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_nothing_at_all(string? host)
    {
        // An empty box means the default host, which is the caller's decision to make. What it
        // must never mean is "send this to a resolver".
        Assert.False(HostAddress.IsPlausible(host));
    }

    [Theory]
    [InlineData("-t")]
    [InlineData("--flood")]
    [InlineData("/t")]
    [InlineData("/n 1000")]
    public void Rejects_anything_shaped_like_a_command_line_switch(string host)
    {
        Assert.False(HostAddress.IsPlausible(host));
    }

    [Theory]
    [InlineData("example .com")]
    [InlineData("example\tcom")]
    [InlineData("example.com\nping evil.test")]
    [InlineData("example.com; rm -rf /")]
    [InlineData("example.com & whoami")]
    [InlineData("$(whoami)")]
    [InlineData("`whoami`")]
    [InlineData("example.com|tee")]
    public void Rejects_separators_and_shell_punctuation(string host)
    {
        Assert.False(HostAddress.IsPlausible(host));
    }

    [Theory]
    [InlineData("example..com")]
    [InlineData(".example.com")]
    [InlineData("example.com-")]
    public void Rejects_malformed_labels(string host)
    {
        Assert.False(HostAddress.IsPlausible(host));
    }

    [Theory]
    [InlineData("...")]
    [InlineData("___")]
    [InlineData("::")]
    public void Rejects_punctuation_with_no_name_in_it(string host)
    {
        // Every character is individually allowed, so only the "at least one letter or digit" rule
        // keeps these out.
        Assert.False(HostAddress.IsPlausible(host));
    }

    [Fact]
    public void Rejects_anything_longer_than_a_dns_name_can_be()
    {
        Assert.True(HostAddress.IsPlausible(new string('a', 253)));
        Assert.False(HostAddress.IsPlausible(new string('a', 254)));
    }

    [Fact]
    public void Ignores_surrounding_whitespace()
    {
        // The settings box trims before storing, but a hand-edited state file does not.
        Assert.True(HostAddress.IsPlausible("  1.1.1.1  "));
        Assert.True(HostAddress.IsPlausible("\texample.com\r\n"));
    }

    [Fact]
    public void Rejects_a_unicode_lookalike_of_a_real_host()
    {
        // Cyrillic 'е' in "examplе.com". Non-ASCII never reaches a resolver from here.
        Assert.False(HostAddress.IsPlausible("examplе.com"));
    }
}
