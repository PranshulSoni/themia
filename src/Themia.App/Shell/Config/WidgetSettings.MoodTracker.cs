using System.Globalization;
using Microsoft.UI.Xaml;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.MoodTracker_MoodSection.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, Str.WidgetsB_Settings_Mood_Span_Label.Text(),
            Str.WidgetsB_Settings_Mood_Span_Desc.Text(),
            FluentLayout.Choice(
                [(MoodSpan.Rolling, Str.WidgetsB_Settings_Mood_RecentWeeks.Text()), (MoodSpan.Month, Str.WidgetsB_Settings_Mood_ThisMonth.Text())],
                config.Span,
                value => { config.Span = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Sort, Str.WidgetsB_Settings_Mood_WeekStart_Label.Text(),
            Str.WidgetsB_Settings_Mood_WeekStart_Desc.Text(),
            FluentLayout.Choice(
                [(MoodWeekStart.Monday, LocaleFormat.WeekdayName(Strings.Current, DayOfWeek.Monday)), (MoodWeekStart.Sunday, LocaleFormat.WeekdayName(Strings.Current, DayOfWeek.Sunday)), (MoodWeekStart.Saturday, LocaleFormat.WeekdayName(Strings.Current, DayOfWeek.Saturday))],
                config.WeekStart,
                value => { config.WeekStart = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Note, Str.WidgetsB_Settings_Mood_Note_Label.Text(),
            Str.WidgetsB_Settings_Mood_Note_Desc.Text(),
            FluentLayout.Toggle(config.ShowNote, value => { config.ShowNote = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, Str.WidgetsB_Settings_Mood_Stats_Label.Text(),
            Str.WidgetsB_Settings_Mood_Stats_Desc.Text(),
            FluentLayout.Toggle(config.ShowStats, value => { config.ShowStats = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsB_Settings_Mood_Weeks_Label.Text(),
            Str.WidgetsB_Settings_Mood_Weeks_Desc.Text(),
            FluentLayout.Slider(
                config.EffectiveHistoryWeeks,
                MoodTrackerConfig.MinHistoryWeeks,
                MoodTrackerConfig.MaxHistoryWeeks,
                1,
                string.Empty,
                value => { config.HistoryWeeks = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.ConfirmRow(
            Glyphs.Clear,
            Str.MoodTracker_ClearBtn.Text(),
            Str.WidgetsB_Settings_Mood_Clear_Desc.Text(),
            Str.Common_Clear.Text(),
            Str.WidgetsB_Settings_Mood_Clear_Question.Text(),
            () => config.Entries.Count == 0
                ? null
                : Str.WidgetsB_Settings_Mood_Clear_Detail.Plural(config.Entries.Count),
            () =>
            {
                // Cleared in place rather than replaced: the list is the config's own and a new one
                // would leave the widget holding the old.
                config.Entries.Clear();
                save();
            }));
    }
}
