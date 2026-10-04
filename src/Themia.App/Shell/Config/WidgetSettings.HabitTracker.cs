using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Habits;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Habits;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-1429] The Habits widget's settings: which list it shows, and how often and how it reads.
/// <para>
/// The habits themselves are added, renamed, moved and deleted <i>in the widget</i>, where the user
/// is looking at them ([S-1425]); a second editor here would be a parallel path to the same data.
/// This page owns the lists — the device-wide pool every Habits widget picks from — and the widget's
/// own settings.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private const string HabitsScope = "habits-settings";

    public static void HabitTracker(List<FrameworkElement> rows, HabitTrackerConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Habit list"));

        if (AppServices.Habits is not { } pool)
        {
            rows.Add(FluentLayout.Note("Habit lists are not available."));
        }
        else
        {
            // The rows change as lists are added and deleted, so they live in one host rebuilt in place.
            var lists = new StackPanel { Spacing = Fluent.Space1 };
            rows.Add(lists);

            void Rebuild()
            {
                lists.Children.Clear();

                foreach (var row in HabitListRows(pool, config, save, Rebuild))
                {
                    lists.Children.Add(row);
                }
            }

            Rebuild();
        }

        rows.Add(FluentLayout.Header("Display"));

        // The interval row only means something under the custom period; it is shown with it.
        var verdict = FluentLayout.Verdict();
        var minutes = new TextBox
        {
            Text = config.Cadence.Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        void CommitMinutes()
        {
            if (!HabitCadence.TryParseMinutes(minutes.Text, out var value, out var refusal))
            {
                FluentLayout.Refuse(verdict, refusal ?? string.Empty);
                return;
            }

            FluentLayout.Confirm(verdict, value == 1 ? "Clears every minute." : $"Clears every {value} minutes.");

            if (value != config.CustomMinutes)
            {
                config.CustomMinutes = value;
                save();
            }
        }

        minutes.LostFocus += (_, _) => CommitMinutes();
        minutes.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                CommitMinutes();
            }
        };

        var interval = FluentLayout.FieldRow(Glyphs.Countdown, "Interval", "Minutes per window, 1 to 1440", minutes, verdict);
        interval.Visibility = config.Cadence.Period == HabitPeriod.Custom ? Visibility.Visible : Visibility.Collapsed;

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Resets", "How often each box clears",
            FluentLayout.Choice<HabitPeriod>(
                [
                    (HabitPeriod.Hourly, "Every hour"),
                    (HabitPeriod.Daily, "Every day"),
                    (HabitPeriod.Weekly, "Every week"),
                    (HabitPeriod.Monthly, "Every month"),
                    (HabitPeriod.Custom, "Custom interval"),
                ],
                config.Cadence.Period,
                value =>
                {
                    config.Period = value;
                    interval.Visibility = value == HabitPeriod.Custom ? Visibility.Visible : Visibility.Collapsed;
                    save();
                })));

        rows.Add(interval);

        rows.Add(FluentLayout.Row(Glyphs.Done, "Show streaks", "The run each habit is on",
            FluentLayout.Toggle(config.ShowStreak, value => { config.ShowStreak = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "History",
            "How many past periods each row shows as dots. Zero hides them; a narrow widget shows fewer",
            FluentLayout.Slider(
                config.EffectiveHistoryDots,
                0,
                HabitRules.MaxHistory,
                1,
                string.Empty,
                value => { config.HistoryDots = (int)Math.Round(value); save(); })));
    }

    /// <summary>
    /// [S-1429] The habit lists on this PC: pick one, rename it as you type, add one, delete one after
    /// asking. A pool every Habits widget shares, so renaming here retitles each widget showing it.
    /// </summary>
    private static IEnumerable<FrameworkElement> HabitListRows(HabitPool pool, HabitTrackerConfig config, Action save, Action rebuild)
    {
        if (pool.Problem is { } problem)
        {
            yield return FluentLayout.Note(problem);
        }

        foreach (var list in pool.Lists)
        {
            var listId = list.Id;

            var pick = new RadioButton
            {
                GroupName = "habit-lists",
                IsChecked = listId == config.ListId,
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };

            pick.Checked += (_, _) =>
            {
                if (config.ListId != listId)
                {
                    config.ListId = listId;
                    save();
                }
            };

            var name = new TextBox
            {
                Text = list.Name,
                PlaceholderText = "List name",
                MinWidth = 160,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = pool.Writable,
            };

            // Written through as it is typed; a blank name is kept so the box can be cleared.
            name.TextChanged += (_, _) => pool.Rename(listId, name.Text);

            var count = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            count.Text = list.Habits.Count == 1 ? "1 habit" : $"{list.Habits.Count} habits";
            count.VerticalAlignment = VerticalAlignment.Center;

            var remove = FluentLayout.IconCommand(Glyphs.Delete, "Delete this list");
            remove.IsEnabled = pool.Writable;
            remove.Click += async (_, _) =>
            {
                try
                {
                    // [S-1429] Always asked, empty or not: it takes every habit and streak in it, and
                    // the button sits one row from the name being typed in.
                    var label = string.IsNullOrWhiteSpace(list.Name) ? "Untitled list" : list.Name;
                    var detail = list.Habits.Count == 0
                        ? $"\"{label}\" will be deleted from this PC."
                        : $"\"{label}\" and its {count.Text}, with their history, will be deleted from this PC. This cannot be undone.";

                    if (!await FluentLayout.ConfirmAsync(remove, "Delete this list?", detail, "Delete").ConfigureAwait(true))
                    {
                        return;
                    }

                    pool.Delete(listId);

                    // Every widget showing it says so rather than being handed another list's habits.
                    rebuild();
                }
                catch (Exception ex)
                {
                    Log.Error(HabitsScope, "could not delete the habit list", ex);
                }
            };

            var line = new Grid
            {
                ColumnSpacing = Fluent.Space2,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            Grid.SetColumn(name, 1);
            Grid.SetColumn(count, 2);
            Grid.SetColumn(remove, 3);
            line.Children.Add(pick);
            line.Children.Add(name);
            line.Children.Add(count);
            line.Children.Add(remove);

            yield return FluentLayout.Card(line);
        }

        if (pool.Lists.Count == 0)
        {
            yield return FluentLayout.Note("No habit lists on this PC yet.");
        }

        var add = new Button { Content = "Add a list", IsEnabled = pool.Writable };
        add.Click += (_, _) =>
        {
            if (pool.Create(HabitLists.NewListName) is { } id)
            {
                config.ListId = id;
                save();
            }

            rebuild();
        };

        yield return add;
    }
}
