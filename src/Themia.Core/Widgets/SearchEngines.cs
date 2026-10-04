using System.Text.Json.Serialization;

namespace Themia.Core.Widgets;

/// <summary>
/// The engines the search box can hand a query to. A closed set, because it reaches a URL that the
/// desktop will open: a free-text provider name is a string that has to be trusted, an enum is not.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SearchEngine>))]
public enum SearchEngine
{
    Google = 0,
    Bing = 1,
    DuckDuckGo = 2,
    Startpage = 3,
    Ecosia = 4,

    /// <summary>A template the user typed. Checked exactly as hard as the built-in ones.</summary>
    Custom = 5,
}

/// <summary>Why a search could not be turned into a URL.</summary>
public enum SearchProblem
{
    None = 0,

    /// <summary>Nothing was typed. Not an error — there is simply nothing to search for.</summary>
    NoQuery = 1,

    /// <summary>The custom template is blank.</summary>
    TemplateEmpty = 2,

    /// <summary>The template has no <c>%s</c>, so the query would be dropped and the engine opened bare.</summary>
    TemplateNoPlaceholder = 3,

    /// <summary>The template has more than one <c>%s</c>. One query, one place to put it.</summary>
    TemplateManyPlaceholders = 4,

    /// <summary>The template is not an http/https address.</summary>
    TemplateBadScheme = 5,

    /// <summary>The <c>%s</c> sits in the host, so the query would decide which site is opened.</summary>
    TemplatePlaceholderInAuthority = 6,

    /// <summary>The filled-in URL was refused by <see cref="LaunchTargets"/>.</summary>
    ResultRefused = 7,
}

/// <summary>A built search URL, or the reason there isn't one.</summary>
/// <param name="Url">The URL to open. Empty whenever <paramref name="Problem"/> is not None.</param>
/// <param name="Problem">Why there is no URL.</param>
public readonly record struct SearchUrl(string Url, SearchProblem Problem)
{
    public bool IsValid => Problem == SearchProblem.None;

    internal static SearchUrl Refuse(SearchProblem problem) => new(string.Empty, problem);
}

/// <summary>
/// Turning what was typed in the box into a URL, safely.
/// <para>
/// This is a second trust boundary on top of <see cref="LaunchTargets"/>, because the search box
/// composes a URL rather than opening one that was already written down. Two things go wrong when
/// that is done casually. The query is pasted in raw, so a query containing <c>&amp;</c> adds a
/// second parameter to somebody else's URL; and the template comes from a text box, so it can name
/// any scheme at all. Both are closed here: the query is percent-encoded, and the template must be
/// an http/https address with exactly one placeholder, in the path or query rather than in the host.
/// </para>
/// </summary>
public static class SearchEngines
{
    /// <summary>Where the query goes in a template. Chosen to match what the original app used.</summary>
    public const string Placeholder = "%s";

    /// <summary>The template a built-in engine uses. <see cref="SearchEngine.Custom"/> has none of its own.</summary>
    public static string Template(SearchEngine engine) => engine switch
    {
        SearchEngine.Google => "https://www.google.com/search?q=%s",
        SearchEngine.Bing => "https://www.bing.com/search?q=%s",
        SearchEngine.DuckDuckGo => "https://duckduckgo.com/?q=%s",
        SearchEngine.Startpage => "https://www.startpage.com/sp/search?query=%s",
        SearchEngine.Ecosia => "https://www.ecosia.org/search?q=%s",
        _ => string.Empty,
    };

    /// <summary>The engine's name, as it is shown in the settings and under the box.</summary>
    public static string Label(SearchEngine engine) => engine switch
    {
        SearchEngine.Google => "Google",
        SearchEngine.Bing => "Bing",
        SearchEngine.DuckDuckGo => "DuckDuckGo",
        SearchEngine.Startpage => "Startpage",
        SearchEngine.Ecosia => "Ecosia",
        SearchEngine.Custom => "Custom",
        _ => "Google",
    };

    /// <summary>Every engine, in the order the settings list them.</summary>
    public static IReadOnlyList<SearchEngine> All { get; } =
    [
        SearchEngine.Google,
        SearchEngine.Bing,
        SearchEngine.DuckDuckGo,
        SearchEngine.Startpage,
        SearchEngine.Ecosia,
        SearchEngine.Custom,
    ];

    /// <summary>
    /// An engine value that is actually one of the closed set. A config file is hand-editable, and
    /// an unknown number must land somewhere rather than throwing on the render path.
    /// </summary>
    public static SearchEngine Resolve(SearchEngine engine)
        => All.Contains(engine) ? engine : SearchEngine.Google;

    /// <summary>
    /// The URL to open for a query, or why there is none.
    /// </summary>
    /// <param name="engine">Which engine. Anything outside the closed set falls back to Google.</param>
    /// <param name="customTemplate">The user's template, consulted only for <see cref="SearchEngine.Custom"/>.</param>
    /// <param name="query">What was typed. Blank is not an error, it is simply nothing to search.</param>
    public static SearchUrl Build(SearchEngine engine, string? customTemplate, string? query)
    {
        var text = (query ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return SearchUrl.Refuse(SearchProblem.NoQuery);
        }

        var resolved = Resolve(engine);

        var template = resolved == SearchEngine.Custom
            ? (customTemplate ?? string.Empty).Trim()
            : Template(resolved);

        var problem = CheckTemplate(template);

        if (problem != SearchProblem.None)
        {
            return SearchUrl.Refuse(problem);
        }

        // Percent-encoding, not concatenation. EscapeDataString leaves only the unreserved
        // characters alone, so "a&b=c" cannot become a second parameter and "a/b" cannot become a
        // second path segment: the whole query lands in the one slot the template left for it.
        var encoded = Uri.EscapeDataString(text);
        var url = template.Replace(Placeholder, encoded, StringComparison.Ordinal);

        // The composed URL goes through the same check as a URL somebody wrote down, because it is
        // about to be handed to the same shell call.
        var classified = LaunchTargets.Classify(url);

        return classified is { IsValid: true, Kind: LaunchKind.Web }
            ? new SearchUrl(classified.Value, SearchProblem.None)
            : SearchUrl.Refuse(SearchProblem.ResultRefused);
    }

    /// <summary>
    /// Whether a template is usable, without needing a query. The settings box calls this so it can
    /// say what is wrong while it is being typed rather than only when a search fails.
    /// </summary>
    public static SearchProblem CheckTemplate(string? candidate)
    {
        var template = (candidate ?? string.Empty).Trim();

        if (template.Length == 0)
        {
            return SearchProblem.TemplateEmpty;
        }

        var first = template.IndexOf(Placeholder, StringComparison.Ordinal);

        if (first < 0)
        {
            return SearchProblem.TemplateNoPlaceholder;
        }

        // Two placeholders means the query is written into the URL twice, and the second one is
        // almost always somewhere the author of the template did not look at.
        if (template.IndexOf(Placeholder, first + Placeholder.Length, StringComparison.Ordinal) >= 0)
        {
            return SearchProblem.TemplateManyPlaceholders;
        }

        var https = template.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var http = template.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        if (!https && !http)
        {
            return SearchProblem.TemplateBadScheme;
        }

        // "https://%s.example.com/" would let the query choose the site. The placeholder has to sit
        // after the authority ends — at the first '/', '?' or '#' following the "://".
        var authorityStart = template.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = template.IndexOfAny(['/', '?', '#'], authorityStart);

        if (authorityEnd < 0 || first < authorityEnd)
        {
            return SearchProblem.TemplatePlaceholderInAuthority;
        }

        // A template with the placeholder already removed still has to be a URL this app would
        // open: the scheme check above is a prefix test, and the rest of the string has had no
        // opinion expressed about it yet.
        var probe = template.Replace(Placeholder, "q", StringComparison.Ordinal);
        var classified = LaunchTargets.Classify(probe);

        return classified is { IsValid: true, Kind: LaunchKind.Web }
            ? SearchProblem.None
            : SearchProblem.TemplateBadScheme;
    }

    /// <summary>A sentence for a problem, shown under the box that produced it.</summary>
    public static string ProblemMessage(SearchProblem problem) => problem switch
    {
        SearchProblem.None => string.Empty,
        SearchProblem.NoQuery => "Type something to search for.",
        SearchProblem.TemplateEmpty => "Enter a search address containing %s.",
        SearchProblem.TemplateNoPlaceholder => "Put %s where the search words should go.",
        SearchProblem.TemplateManyPlaceholders => "Use %s once only.",
        SearchProblem.TemplateBadScheme => "The address must start with https:// or http://.",
        SearchProblem.TemplatePlaceholderInAuthority => "%s cannot be part of the site name.",
        SearchProblem.ResultRefused => "That search would not make a usable web address.",
        _ => "That search could not be opened.",
    };
}
