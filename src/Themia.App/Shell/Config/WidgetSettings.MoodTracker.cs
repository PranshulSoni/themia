using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
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

        rows.Add(FluentLayout.Row(Glyphs.Count, "Weeks of history",
            "The most the rolling heatmap shows; a small widget shows fewer",
            FluentLayout.Slider(
                config.EffectiveHistoryWeeks,
                MoodTrackerConfig.MinHistoryWeeks,
                MoodTrackerConfig.MaxHistoryWeeks,
                1,
                string.Empty,
                value => { config.HistoryWeeks = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Note, "Show the thought box",
            "A line about today's mood, kept with it",
            FluentLayout.Toggle(config.ShowNote, value => { config.ShowNote = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show the run and average",
            "Hidden automatically when the widget is too short for it",
            FluentLayout.Toggle(config.ShowStats, value => { config.ShowStats = value; save(); })));

        var clear = new Button
        {
            Content = "Clear history",

            // Disabled rather than merely ineffective: a button that can only tell you there was
            // nothing to do is a button that should not have been offered.
            IsEnabled = config.Entries.Count > 0,
        };

        clear.Click += (_, _) => _ = ConfirmClearMoodsAsync(clear, config, save);

        rows.Add(FluentLayout.Row(Glyphs.Clear, "Clear history",
            "Deletes every mood logged in this widget. This cannot be undone", clear));
    }

    /// <summary>
    /// Ask, then clear. Awaited rather than fired and forgotten, so a dialog that could not be shown
    /// is never mistaken for the user saying yes.
    /// </summary>
    private static async Task ConfirmClearMoodsAsync(Button source, MoodTrackerConfig config, Action save)
    {
        var root = source.XamlRoot;

        if (root is null)
        {
            // No window to put a dialog in. Refusing beats clearing unasked.
            Log.Warn(DeskScope, "no XamlRoot for the clear-moods confirmation; the history was left alone");
            return;
        }

        if (config.Entries.Count == 0)
        {
            return;
        }

        var count = config.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = source.ActualTheme,
            Title = "Clear every mood?",
            Content = $"All {count} logged moods and their notes are deleted. This cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            // ShowAsync throws when another dialog already owns this window. Say so and change
            // nothing.
            Log.Error(DeskScope, "could not show the clear-moods confirmation", ex);
            return;
        }

        // Cleared in place rather than replaced: the list is the config's own and a new one would
        // leave the widget holding the old.
        config.Entries.Clear();
        source.IsEnabled = false;
        save();
    }
}
