using Microsoft.UI;
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
/// is nowhere else for that history to be.
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

        // One host whose children are rebuilt in place, because adding, removing and reordering all
        // change the shape of this section and the page around it is built once.
        var host = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(host);

        var empty = new TextBlock
        {
            Text = "No habits yet.",
            Opacity = 0.65,
            Margin = new Thickness(0, Fluent.Space1, 0, Fluent.Space1),
        };

        void Rebuild()
        {
            host.Children.Clear();

            if (config.Habits.Count == 0)
            {
                host.Children.Add(empty);
                return;
            }

            for (var index = 0; index < config.Habits.Count; index++)
            {
                host.Children.Add(HabitCard(config, index, save, Rebuild));
            }
        }

        Rebuild();

        var add = new Button { Content = "Add habit" };

        add.Click += (_, _) =>
        {
            config.Habits.Add(new HabitRecord { Id = HabitRecord.NewId(), Name = string.Empty });
            save();
            Rebuild();
        };

        rows.Add(FluentLayout.Row(Glyphs.Add, "Add habit", "One line a day, ticked off on the widget", add));

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

    /// <summary>
    /// One habit's editor: its name, where it sits in the list, and the way out.
    /// <para>
    /// Deleting is behind a confirmation because it takes the habit's whole history with it and
    /// there is no undo anywhere in this app. The days live inside the habit, so there is no second
    /// place to prune and no way to end up with a run of days belonging to nothing.
    /// </para>
    /// </summary>
    private static FrameworkElement HabitCard(HabitTrackerConfig config, int index, Action save, Action rebuild)
    {
        var habit = config.Habits[index];

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

        var up = IconButton(new FontIcon { Glyph = Glyphs.MoveUp, FontSize = 14 }, "Move up");
        var down = IconButton(new FontIcon { Glyph = Glyphs.MoveDown, FontSize = 14 }, "Move down");
        var remove = IconButton(new FontIcon { Glyph = Glyphs.Delete, FontSize = 14 }, "Remove habit");

        // One swap per click, and the ends cannot move past themselves.
        up.IsEnabled = index > 0;
        down.IsEnabled = index < config.Habits.Count - 1;

        up.Click += (_, _) => SwapHabits(config, index, index - 1, save, rebuild);
        down.Click += (_, _) => SwapHabits(config, index, index + 1, save, rebuild);

        remove.Click += (_, _) => _ = ConfirmRemoveHabitAsync(remove, config, habit, save, rebuild);

        var days = HabitRules.Days(habit.Days, LocalDay.From(DateTimeOffset.Now));

        var summary = new TextBlock
        {
            Text = days.Count switch
            {
                0 => "Not started",
                1 => "1 day recorded",
                _ => $"{days.Count} days recorded",
            },
            FontSize = 12,
            Opacity = 0.65,
            Margin = new Thickness(0, Fluent.Space2, 0, 0),
        };

        var top = new Grid
        {
            ColumnSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(up, 1);
        Grid.SetColumn(down, 2);
        Grid.SetColumn(remove, 3);

        top.Children.Add(name);
        top.Children.Add(up);
        top.Children.Add(down);
        top.Children.Add(remove);

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(summary);

        // The same card the stock settings rows use, so the list reads as part of the page rather
        // than as a panel bolted onto it.
        return new Border
        {
            Child = body,
            Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space4, Fluent.Space3),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Background = FluentLayout.Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(Fluent.Hairline),
        };
    }

    private static void SwapHabits(HabitTrackerConfig config, int from, int to, Action save, Action rebuild)
    {
        if (to < 0 || to >= config.Habits.Count || from == to)
        {
            return;
        }

        (config.Habits[from], config.Habits[to]) = (config.Habits[to], config.Habits[from]);
        save();
        rebuild();
    }

    /// <summary>
    /// Ask, then remove. Awaited rather than fired and forgotten, so a dialog that could not be
    /// shown is never mistaken for the user saying yes.
    /// </summary>
    private static async Task ConfirmRemoveHabitAsync(
        Button source,
        HabitTrackerConfig config,
        HabitRecord habit,
        Action save,
        Action rebuild)
    {
        var root = source.XamlRoot;

        if (root is null)
        {
            // No window to put a dialog in. Refusing beats deleting unasked.
            Themia.Core.Diagnostics.Log.Warn(
                DeskScope,
                "no XamlRoot for the remove-habit confirmation; the habit was left alone");

            return;
        }

        var label = string.IsNullOrWhiteSpace(habit.Name) ? "this habit" : $"“{habit.Name}”";
        var recorded = habit.Days.Count;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = source.ActualTheme,
            Title = "Remove this habit?",
            Content = recorded == 0
                ? $"{label} has nothing recorded yet."
                : $"{label} and its {recorded.ToString(System.Globalization.CultureInfo.InvariantCulture)} recorded days are deleted. This cannot be undone.",
            PrimaryButtonText = "Remove",
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
            Themia.Core.Diagnostics.Log.Error(DeskScope, "could not show the remove-habit confirmation", ex);
            return;
        }

        // Removed by identity rather than by the index the card was built with: the list may have
        // been reordered while the dialog was up.
        config.Habits.Remove(habit);
        save();
        rebuild();
    }
}
