using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// A box that opens a query in the default browser.
/// <para>
/// The engine is an enum rather than a URL, so the common case cannot be misconfigured at all. The
/// custom template exists because people have their own search, and it is the one field here that
/// is a trust boundary: it composes a URL the desktop will open, so it is checked by
/// <see cref="SearchEngines.CheckTemplate"/> before it is ever used and again when it is filled in.
/// </para>
/// </summary>
public sealed class WebSearchConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.WebSearch;

    /// <summary>Which engine the box hands the query to.</summary>
    public SearchEngine Engine { get; set; } = SearchEngine.Google;

    /// <summary>
    /// The user's own search address, with <c>%s</c> where the words go. Consulted only when
    /// <see cref="Engine"/> is <see cref="SearchEngine.Custom"/>.
    /// </summary>
    public string CustomTemplate { get; set; } = string.Empty;

    /// <summary>Empty the box once the search has been opened.</summary>
    public bool ClearAfterSearch { get; set; } = true;

    /// <summary>The engine actually in use: an unknown value from a hand-edited file lands on Google.</summary>
    [JsonIgnore]
    public SearchEngine EffectiveEngine => SearchEngines.Resolve(Engine);

    public override WidgetConfig Clone() => new WebSearchConfig
    {
        Engine = Engine,
        CustomTemplate = CustomTemplate,
        ClearAfterSearch = ClearAfterSearch,
    };
}
