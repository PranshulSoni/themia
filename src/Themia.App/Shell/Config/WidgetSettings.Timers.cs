using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The settings rows for the two timer widgets.
/// <para>
/// Both write straight to the live config and save as they go, like every other section: the widget
/// is on the desktop behind this window, and a setting you cannot see take effect is a setting you
/// cannot judge. Nothing here keeps a copy of a config value — the controls are seeded from the
/// config once and every handler writes back to it, so there is no second copy to fall out of step.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private static readonly IReadOnlyList<(CountdownFormat Value, string Label)> CountdownFormats =
    [
        (CountdownFormat.Auto, CountdownFormatter.FormatLabel(CountdownFormat.Auto)),
        (CountdownFormat.Days, CountdownFormatter.FormatLabel(CountdownFormat.Days)),
        (CountdownFormat.DaysHoursMinutes, CountdownFormatter.FormatLabel(CountdownFormat.DaysHoursMinutes)),
        (CountdownFormat.DaysHoursMinutesSeconds, CountdownFormatter.FormatLabel(CountdownFormat.DaysHoursMinutesSeconds)),
    ];

    public static void Countdown(List<FrameworkElement> rows, CountdownConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Timers"));

        // One host whose children are rebuilt in place, because adding, removing and reordering all
        // change the shape of this section and the page around it is built once.
        var host = new StackPanel { Spacing = 4 };
        rows.Add(host);

        var empty = new TextBlock
        {
            Text = "No timers yet.",
            Opacity = 0.65,
            Margin = new Thickness(0, 4, 0, 4),
        };

        void Rebuild()
        {
            host.Children.Clear();

            if (config.Timers.Count == 0)
            {
                host.Children.Add(empty);
                return;
            }

            for (var index = 0; index < config.Timers.Count; index++)
            {
                host.Children.Add(TimerCard(config, index, save, Rebuild));
            }
        }

        Rebuild();

        var add = new Button { Content = "Add timer" };

        add.Click += (_, _) =>
        {
            // [C-51] This time tomorrow, on the minute: far enough out to be visible in every
            // format, near enough to be obviously a placeholder.
            config.Timers.Add(CountdownTimer.Create(string.Empty, CountdownFormatter.DefaultTarget(DateTimeOffset.Now)));
            save();
            Rebuild();
        };

        rows.Add(FluentLayout.Row(Glyphs.Add, "Add timer", "Counts down to a date and time you choose", add));
    }

    /// <summary>
    /// One timer's editor: its name, where it points, how it reads, and where it sits in the list.
    /// [C-50]
    /// </summary>
    private static FrameworkElement TimerCard(CountdownConfig config, int index, Action save, Action rebuild)
    {
        var timer = config.Timers[index];

        var label = new TextBox
        {
            Text = timer.Label,
            PlaceholderText = CountdownFormatter.UntitledLabel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // [C-54] On lost focus and on Enter, never per keystroke: a rebuild between two letters
        // takes the caret with it, and a save per letter writes the file a dozen times a word.
        void CommitLabel()
        {
            if (label.Text == timer.Label)
            {
                return;
            }

            timer.Label = label.Text;
            save();
        }

        label.LostFocus += (_, _) => CommitLabel();
        label.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                CommitLabel();
                args.Handled = true;
            }
        };

        var zone = TimeZoneInfo.Local;

        var date = new CalendarDatePicker
        {
            Date = CountdownFormatter.LocalDate(timer.Target, zone),
            PlaceholderText = "Pick a date",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var time = new TimePicker
        {
            SelectedTime = CountdownFormatter.LocalTimeOfDay(timer.Target, zone),
            ClockIdentifier = "24HourClock",
            MinuteIncrement = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        void CommitTarget()
        {
            var next = CountdownFormatter.CombineLocal(date.Date, time.SelectedTime, zone);

            // [C-55] A cleared or impossible value leaves the target exactly as it was. Writing
            // "now" here is what the original did, and it silently retargeted the timer mid-edit.
            if (next is not { } target || target == timer.Target)
            {
                return;
            }

            timer.Target = target;
            save();
        }

        date.DateChanged += (_, _) => CommitTarget();
        time.SelectedTimeChanged += (_, _) => CommitTarget();

        var format = FluentLayout.Choice(CountdownFormats, timer.Format, value =>
        {
            timer.Format = value;
            save();
        });

        format.HorizontalAlignment = HorizontalAlignment.Stretch;
        format.MinWidth = 0;

        var up = IconButton(new FontIcon { Glyph = Glyphs.MoveUp, FontSize = 14 }, "Move up");
        var down = IconButton(new FontIcon { Glyph = Glyphs.MoveDown, FontSize = 14 }, "Move down");
        var remove = IconButton(new FontIcon { Glyph = Glyphs.Delete, FontSize = 14 }, "Remove timer");

        // [C-52] One swap per click, and the ends cannot move past themselves.
        up.IsEnabled = index > 0;
        down.IsEnabled = index < config.Timers.Count - 1;

        up.Click += (_, _) => Swap(config, index, index - 1, save, rebuild);
        down.Click += (_, _) => Swap(config, index, index + 1, save, rebuild);

        remove.Click += (_, _) =>
        {
            config.Timers.RemoveAt(index);
            save();
            rebuild();
        };

        var top = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(up, 1);
        Grid.SetColumn(down, 2);
        Grid.SetColumn(remove, 3);

        top.Children.Add(label);
        top.Children.Add(up);
        top.Children.Add(down);
        top.Children.Add(remove);

        var bottom = new Grid
        {
            ColumnSpacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) },
            },
        };

        Grid.SetColumn(date, 0);
        Grid.SetColumn(time, 1);
        Grid.SetColumn(format, 2);

        bottom.Children.Add(date);
        bottom.Children.Add(time);
        bottom.Children.Add(format);

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(bottom);

        // The same card the stock settings rows use, so a list of timers reads as part of the page
        // rather than as a panel bolted onto it.
        return new Border
        {
            Child = body,
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(4),
            Background = FluentLayout.Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
        };
    }

    private static void Swap(CountdownConfig config, int from, int to, Action save, Action rebuild)
    {
        if (to < 0 || to >= config.Timers.Count || from == to)
        {
            return;
        }

        (config.Timers[from], config.Timers[to]) = (config.Timers[to], config.Timers[from]);
        save();
        rebuild();
    }

    private static Button IconButton(IconElement icon, string tooltip)
    {
        var button = new Button
        {
            Content = icon,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    public static void Pomodoro(List<FrameworkElement> rows, PomodoroConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Pomodoro"));

        // [S-01] Seeded from the clamped accessors, so a value that got onto disk out of range shows
        // as the value actually in use rather than snapping when the slider is first touched.
        rows.Add(FluentLayout.Row(Glyphs.Pomodoro, "Focus", "How long a work phase runs",
            FluentLayout.Slider(config.EffectiveWorkMinutes, 1, 180, 1, " min",
                value => { config.WorkMinutes = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Break", "How long a break runs",
            FluentLayout.Slider(config.EffectiveBreakMinutes, 1, 180, 1, " min",
                value => { config.BreakMinutes = (int)Math.Round(value); save(); })));

        // [S-06] The cue sounds on the phase change only — not on start, pause or reset.
        rows.Add(FluentLayout.Row(Glyphs.Sound, "Sound", "A short cue when the phase changes",
            FluentLayout.Toggle(config.Alarm, value => { config.Alarm = value; save(); })));
    }
}
