namespace Themia.App.Widgets;

/// <summary>One command a view adds to its card's right-click menu.</summary>
/// <param name="Glyph">Segoe Fluent Icons code point.</param>
/// <param name="Text">The menu text, which may name the current state ("Pause", "Resume").</param>
/// <param name="Invoke">Runs on the UI thread when the item is clicked.</param>
internal readonly record struct WidgetAction(string Glyph, string Text, Action Invoke);

/// <summary>
/// A view with commands that belong on the widget itself rather than in its settings: pausing a
/// camera, refreshing a page now. A <i>state</i> the user flips while looking at the widget, not a
/// setting they configure once.
/// <para>
/// Read each time the menu opens, so the text can follow the state. The card puts these at the top
/// of the menu, under Configure, and owns everything else on it.
/// </para>
/// </summary>
internal interface IWidgetContextActions
{
    IReadOnlyList<WidgetAction> ContextActions { get; }
}
