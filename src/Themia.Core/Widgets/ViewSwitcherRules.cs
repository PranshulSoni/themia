using System.Globalization;

namespace Themia.Core.Widgets;

/// <summary>A view as the switcher sees it: enough to draw a button, nothing to edit.</summary>
/// <param name="Icon">[S-903] The view's glyph, or null for its initials.</param>
/// <param name="WidgetCount">[S-1657] How many widgets the view holds, for a button's "3 widgets" line.</param>
public readonly record struct ViewSummary(string Id, string Name, string? Icon = null, int WidgetCount = 0);

/// <summary>One button on the switcher.</summary>
/// <param name="Id">The view it shows.</param>
/// <param name="Name">The view's name, for the label and the tooltip.</param>
/// <param name="Initials">What the button says when there is no room for the name.</param>
/// <param name="Shown">Whether the widget's own display is showing this view right now.</param>
/// <param name="Icon">[S-903] The view's glyph, drawn in place of the initials; null for none.</param>
/// <param name="WidgetCount">[S-1657] How many widgets the view holds.</param>
public readonly record struct ViewChoice(string Id, string Name, string Initials, bool Shown, string? Icon = null, int WidgetCount = 0);

/// <summary>
/// The rules behind the view switcher widget: which views it offers, in what order, what a button
/// says, and how the buttons are arranged. Pure, so the decisions are tested without a window.
/// </summary>
public static class ViewSwitcherRules
{
    /// <summary>
    /// [S-489] The letters a button falls back to: the first letter of each of the first two words,
    /// or the first two letters of a single word, upper-cased. "?" for a blank name, so a button is
    /// never empty.
    /// <para>
    /// Counted in text elements rather than UTF-16 units, so a name that starts with an emoji or an
    /// accented letter built from two code points is not cut in half.
    /// </para>
    /// </summary>
    public static string Initials(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "?";
        }

        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var raw = words.Length >= 2
            ? FirstElements(words[0], 1) + FirstElements(words[1], 1)
            : FirstElements(trimmed, 2);

        return raw.ToUpperInvariant();
    }

    private static string FirstElements(string text, int count)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var result = string.Empty;

        for (var i = 0; i < count && enumerator.MoveNext(); i++)
        {
            result += enumerator.GetTextElement();
        }

        return result;
    }

    /// <summary>
    /// [S-488] The views the switcher offers, in the user's own view order, with the one this
    /// display is showing marked.
    /// <para>
    /// An empty selection means every view, so a view created later appears without a trip to the
    /// settings. A selection whose views have all been deleted also means every view: a switcher
    /// with no buttons is a widget that has silently stopped working.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ViewChoice> Choices(
        IReadOnlyList<ViewSummary> views,
        IReadOnlyCollection<string>? visible,
        string? shownViewId)
    {
        ArgumentNullException.ThrowIfNull(views);

        var effective = EffectiveVisible(views, visible);
        var choices = new List<ViewChoice>(views.Count);

        foreach (var view in views)
        {
            if (!effective.Contains(view.Id))
            {
                continue;
            }

            choices.Add(new ViewChoice(
                view.Id,
                view.Name,
                Initials(view.Name),
                string.Equals(view.Id, shownViewId, StringComparison.Ordinal),
                string.IsNullOrEmpty(view.Icon) ? null : view.Icon,
                Math.Max(0, view.WidgetCount)));
        }

        return choices;
    }

    /// <summary>The ids actually on offer: the selection, or every view when it names none that exist.</summary>
    public static HashSet<string> EffectiveVisible(IReadOnlyList<ViewSummary> views, IReadOnlyCollection<string>? visible)
    {
        ArgumentNullException.ThrowIfNull(views);

        var all = new HashSet<string>(views.Select(v => v.Id), StringComparer.Ordinal);

        if (visible is null || visible.Count == 0)
        {
            return all;
        }

        var chosen = new HashSet<string>(visible.Where(all.Contains), StringComparer.Ordinal);
        return chosen.Count == 0 ? all : chosen;
    }

    /// <summary>
    /// [S-490] The selection after ticking or unticking one view in the settings.
    /// <para>
    /// The last view cannot be unticked — the switcher would have nothing to switch to — so that
    /// returns the selection unchanged. Ticking every view collapses back to the empty "all"
    /// selection, so the switcher goes on following views that are added later.
    /// </para>
    /// </summary>
    public static List<string> Toggle(
        IReadOnlyList<ViewSummary> views,
        IReadOnlyCollection<string>? visible,
        string viewId,
        bool on)
    {
        ArgumentNullException.ThrowIfNull(views);

        var effective = EffectiveVisible(views, visible);

        if (on)
        {
            effective.Add(viewId);
        }
        else if (effective.Count > 1)
        {
            effective.Remove(viewId);
        }

        // Back in view order, and back to "all" when nothing is left out.
        var ordered = views.Select(v => v.Id).Where(effective.Contains).ToList();
        return ordered.Count == views.Count ? [] : ordered;
    }

    /// <summary>
    /// [S-1657] The line under a view's name: "No widgets", "1 widget", "N widgets". A negative count
    /// (never stored, but the summary is plain data) reads as none.
    /// </summary>
    public static string CountText(int count) => count switch
    {
        <= 0 => "No widgets",
        1 => "1 widget",
        _ => count.ToString(CultureInfo.CurrentCulture) + " widgets",
    };
}
