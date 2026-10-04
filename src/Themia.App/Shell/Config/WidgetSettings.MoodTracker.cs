using System.Globalization;
using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Mood widget's settings rows.
/// <para>
/// The moods themselves are logged on the widget — that is the whole gesture — so this owns only
/// the shape of the heatmap and the one destructive action, which is behind a confirmation because
/// it takes every mood ever logged and there is no undo anywhere in this app.
/// </para>
/// <para>
/// The week-start row exists because there is no culture to ask. The app builds with
/// <c>InvariantGlobalization</c>, and the invariant culture answers "Sunday" to everyone, which is
/// wrong for most of the world. Guessing silently would put every column of the heatmap under the
/// wrong weekday.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void MoodTracker(List<FrameworkElement> rows, MoodTrackerConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Mood"));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Heatmap shows",
            "A rolling window, or one calendar month at a time",
            FluentLayout.Choice(
                [(MoodSpan.Rolling, "Recent weeks"), (MoodSpan.Month, "This month")],
                config.Span,
                value => { config.Span = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Sort, "Week starts on",
            "Which weekday the heatmap's first column is",
            FluentLayout.Choice(
                [(MoodWeekStart.Monday, "Monday"), (MoodWeekStart.Sunday, "Sunday"), (MoodWeekStart.Saturday, "Saturday")],
                config.WeekStart,
                value => { config.WeekStart = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Note, "Show the thought box",
            "A line about today's mood, kept with it",
            FluentLayout.Toggle(config.ShowNote, value => { config.ShowNote = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show the run and average",
            "Hidden automatically when the widget is too short for it",
            FluentLayout.Toggle(config.ShowStats, value => { config.ShowStats = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Weeks of history",
            "The most the rolling heatmap shows; a small widget shows fewer",
            FluentLayout.Slider(
                config.EffectiveHistoryWeeks,
                MoodTrackerConfig.MinHistoryWeeks,
                MoodTrackerConfig.MaxHistoryWeeks,
                1,
                string.Empty,
                value => { config.HistoryWeeks = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.ConfirmRow(
            Glyphs.Clear,
            "Clear history",
            "Deletes every mood logged in this widget. This cannot be undone",
            "Clear",
            "Clear every mood?",
            () => config.Entries.Count == 0
                ? null
                : $"All {config.Entries.Count.ToString(CultureInfo.InvariantCulture)} logged moods and their notes are deleted. This cannot be undone.",
            () =>
            {
                // Cleared in place rather than replaced: the list is the config's own and a new one
                // would leave the widget holding the old.
                config.Entries.Clear();
                save();
            }));
    }
}
