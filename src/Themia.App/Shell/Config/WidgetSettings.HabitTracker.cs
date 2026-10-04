using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Habit widget's settings rows: which habits there are, and how the list reads.
/// <para>
/// The habits are edited <i>here</i> and ticked off on the widget. Splitting it that way is
/// deliberate: a widget sits under the pointer's path across the whole desktop and has no undo, so
/// the gestures that can destroy a month of history belong behind a window that has to be opened on
/// purpose, while the one gesture that is the whole point of the widget — the daily box — stays one
/// click away.
/// </para>
/// <para>
/// Both sides write the same <see cref="HabitTrackerConfig"/> object, so they cannot disagree, and a
/// habit's days travel inside the habit — deleting one here takes its history with it because there
/// is nowhere else for that history to be. That is why this is the one list on any settings page
/// whose remove asks first.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void HabitTracker(List<FrameworkElement> rows, HabitTrackerConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Habits"));

        FluentLayout.ListEditor(rows, new ListEditorOptions<HabitRecord>
        {
            Items = config.Habits,
            Save = save,
            EmptyText = "No habits yet.",
            AddTitle = "Add a habit",
            AddDescription = "One line a day, ticked off on the widget",
            AddAction = "Add",
            RemoveTooltip = "Remove habit",
            Create = () => new HabitRecord { Id = HabitRecord.NewId(), Name = string.Empty },
            Body = habit => HabitBody(habit, save),

            // Removing takes the habit's whole history with it and there is no undo anywhere in this
            // app, so it goes through the same confirmation every other destructive action does.
            ConfirmRemove = (habit, source) => FluentLayout.ConfirmAsync(
                source,
                "Remove this habit?",
                RemovalDetail(habit),
                "Remove"),
        });

        rows.Add(FluentLayout.Row(Glyphs.Done, "Show streaks", "The run of days each habit is on",
            FluentLayout.Toggle(config.ShowStreak, value => { config.ShowStreak = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Days of history",
            "How many past days each row shows as squares. Zero hides them",
            FluentLayout.Slider(
                config.EffectiveHistoryDays,
                0,
                HabitRules.MaxHistoryDays,
                1,
                string.Empty,
                value => { config.HistoryDays = (int)Math.Round(value); save(); })));
    }

    /// <summary>One habit's editor: its name, and how much of it there is.</summary>
    private static FrameworkElement HabitBody(HabitRecord habit, Action save)
    {
        var name = new TextBox
        {
            Text = habit.Name,
            PlaceholderText = "Untitled habit",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // On lost focus and on Enter, never per keystroke: a rebuild between two letters takes the
        // caret with it, and a save per letter writes the state file a dozen times a word.
        void CommitName()
        {
            if (name.Text == habit.Name)
            {
                return;
            }

            habit.Name = name.Text;
            save();
        }

        name.LostFocus += (_, _) => CommitName();
        name.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                CommitName();
            }
        };

        var days = HabitRules.Days(habit.Days, LocalDay.From(DateTimeOffset.Now));

        var summary = new TextBlock
        {
            Text = days.Count switch
            {
                0 => "Not started",
                1 => "1 day recorded",
                _ => $"{days.Count.ToString(CultureInfo.InvariantCulture)} days recorded",
            },
            FontSize = 12,
            Opacity = 0.65,
            Margin = new Thickness(0, Fluent.Space2, 0, 0),
        };

        return new StackPanel { Children = { name, summary } };
    }

    /// <summary>What the confirmation says is about to go, which is the habit and its whole record.</summary>
    private static string RemovalDetail(HabitRecord habit)
    {
        var label = string.IsNullOrWhiteSpace(habit.Name) ? "this habit" : $"“{habit.Name}”";
        var recorded = habit.Days.Count;

        return recorded == 0
            ? $"{label} has nothing recorded yet."
            : $"{label} and its {recorded.ToString(CultureInfo.InvariantCulture)} recorded days are deleted. This cannot be undone.";
    }
}
