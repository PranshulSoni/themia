using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Composing a URL from a query box and a template. The second trust boundary in the launcher
/// widgets: unlike a shortcut, this one <i>builds</i> the URL, so a query that is pasted in raw or
/// a template that names any scheme it likes both end at the shell.
/// </summary>
public class SearchEngineTests
{
    // ---- encoding ------------------------------------------------------------------------------

    [Fact]
    public void A_query_cannot_smuggle_a_second_parameter()
    {
        var built = SearchEngines.Build(SearchEngine.Google, null, "cats&tbm=isch&safe=off");

        Assert.True(built.IsValid);

        // One '=' for the engine's own q parameter, and one '&' would mean a second parameter.
        Assert.DoesNotContain("&", built.Url, StringComparison.Ordinal);
        Assert.Contains("cats%26tbm%3Disch%26safe%3Doff", built.Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("a#b", "a%23b")]
    [InlineData("a?b", "a%3Fb")]
    [InlineData("a+b", "a%2Bb")]
    [InlineData("100%", "100%25")]
    public void Every_reserved_character_in_a_query_is_percent_encoded(string query, string expected)
    {
        var built = SearchEngines.Build(SearchEngine.DuckDuckGo, null, query);

        Assert.True(built.IsValid);
        Assert.Equal("https://duckduckgo.com/?q=" + expected, built.Url);
    }

    [Fact]
    public void A_query_is_trimmed_before_it_is_encoded()
    {
        var built = SearchEngines.Build(SearchEngine.Bing, null, "   cats   ");

        Assert.True(built.IsValid);
        Assert.Equal("https://www.bing.com/search?q=cats", built.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void A_blank_query_opens_nothing(string? query)
    {
        var built = SearchEngines.Build(SearchEngine.Google, null, query);

        Assert.False(built.IsValid);
        Assert.Equal(SearchProblem.NoQuery, built.Problem);
        Assert.Equal(string.Empty, built.Url);
    }

    [Fact]
    public void A_very_long_query_is_refused_rather_than_truncated()
    {
        // Truncating would search for something other than what was typed, silently.
        var built = SearchEngines.Build(SearchEngine.Google, null, new string('a', SearchEngines.Placeholder.Length + 4000));

        Assert.False(built.IsValid);
        Assert.Equal(SearchProblem.ResultRefused, built.Problem);
    }

    // ---- the built-in engines -------------------------------------------------------------------

    [Fact]
    public void Every_built_in_engine_builds_a_url_the_launcher_would_open()
    {
        foreach (var engine in SearchEngines.All)
        {
            if (engine == SearchEngine.Custom)
            {
                continue;
            }

            var built = SearchEngines.Build(engine, null, "themia");

            Assert.True(built.IsValid, SearchEngines.Label(engine));

            var classified = LaunchTargets.Classify(built.Url);

            Assert.True(classified.IsValid, built.Url);
            Assert.Equal(LaunchKind.Web, classified.Kind);
        }
    }

    [Fact]
    public void Every_built_in_template_passes_the_template_check()
    {
        foreach (var engine in SearchEngines.All)
        {
            if (engine == SearchEngine.Custom)
            {
                continue;
            }

            Assert.Equal(SearchProblem.None, SearchEngines.CheckTemplate(SearchEngines.Template(engine)));
        }
    }

    [Fact]
    public void An_engine_outside_the_closed_set_falls_back_to_google_rather_than_throwing()
    {
        // The config file is hand-editable, so an unknown number has to land somewhere.
        var built = SearchEngines.Build((SearchEngine)9999, null, "cats");

        Assert.True(built.IsValid);
        Assert.StartsWith("https://www.google.com/search?q=", built.Url, StringComparison.Ordinal);
        Assert.Equal(SearchEngine.Google, SearchEngines.Resolve((SearchEngine)9999));
    }

    // ---- the custom template ---------------------------------------------------------------------

    [Fact]
    public void A_usable_custom_template_is_filled_in()
    {
        var built = SearchEngines.Build(SearchEngine.Custom, "https://example.com/find?q=%s&lang=en", "cats");

        Assert.True(built.IsValid);
        Assert.Equal("https://example.com/find?q=cats&lang=en", built.Url);
    }

    [Theory]
    [InlineData("https://example.com/find?q=%s&also=%s", SearchProblem.TemplateManyPlaceholders)]
    [InlineData("https://example.com/find?q=%s&x=%s&y=%s", SearchProblem.TemplateManyPlaceholders)]
    public void A_template_with_more_than_one_placeholder_is_refused(string template, SearchProblem expected)
    {
        Assert.Equal(expected, SearchEngines.CheckTemplate(template));
        Assert.Equal(expected, SearchEngines.Build(SearchEngine.Custom, template, "cats").Problem);
    }

    [Fact]
    public void A_template_with_no_placeholder_is_refused_rather_than_opening_the_engine_bare()
    {
        Assert.Equal(SearchProblem.TemplateNoPlaceholder, SearchEngines.CheckTemplate("https://example.com/find?q=cats"));
    }

    [Theory]
    [InlineData("javascript:alert(%s)")]
    [InlineData("data:text/html,%s")]
    [InlineData("file:///C:/%s")]
    [InlineData("vbscript:%s")]
    [InlineData("steam://run/%s")]
    [InlineData("ftp://example.com/%s")]
    [InlineData("example.com/find?q=%s")]
    public void A_template_that_is_not_an_http_address_is_refused(string template)
    {
        Assert.Equal(SearchProblem.TemplateBadScheme, SearchEngines.CheckTemplate(template));
        Assert.Equal(SearchProblem.TemplateBadScheme, SearchEngines.Build(SearchEngine.Custom, template, "cats").Problem);
    }

    [Theory]
    [InlineData("https://%s.example.com/find")]
    [InlineData("https://%s/find")]
    [InlineData("http://%s.evil.example/")]
    [InlineData("https://example.com%s")]
    public void A_placeholder_inside_the_host_is_refused(string template)
    {
        // Otherwise the query chooses which site is opened.
        Assert.Equal(SearchProblem.TemplatePlaceholderInAuthority, SearchEngines.CheckTemplate(template));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_custom_template_is_refused(string? template)
    {
        Assert.Equal(SearchProblem.TemplateEmpty, SearchEngines.CheckTemplate(template));
        Assert.Equal(SearchProblem.TemplateEmpty, SearchEngines.Build(SearchEngine.Custom, template, "cats").Problem);
    }

    [Fact]
    public void A_template_carrying_a_control_character_is_refused()
    {
        Assert.NotEqual(SearchProblem.None, SearchEngines.CheckTemplate("https://example.com/\nfind?q=%s"));
    }

    [Fact]
    public void Every_problem_has_a_sentence()
    {
        foreach (SearchProblem problem in Enum.GetValues<SearchProblem>())
        {
            var message = SearchEngines.ProblemMessage(problem);

            if (problem == SearchProblem.None)
            {
                Assert.Equal(string.Empty, message);
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(message));
        }
    }

    [Fact]
    public void Every_engine_in_the_closed_set_has_a_label()
    {
        foreach (var engine in SearchEngines.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(SearchEngines.Label(engine)));
        }
    }
}
