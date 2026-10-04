using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Countdown settings rows: the timer list and each timer's editor.
/// <para>
/// Rows write straight to the live config and save as they go, like every other section: the widget
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

        // [C-50] [C-52] The list, its ordering and its removals are the shared editor's, so this
        // page carries only the part of a timer that is a timer: where it points and how it reads.
        FluentLayout.ListEditor(rows, new ListEditorOptions<CountdownTimer>
        {
            Items = config.Timers,
            Save = save,
            EmptyText = "No timers yet.",
            AddTitle = "Add a timer",
            AddDescription = "Counts down to a date and time you choose",
            AddAction = "Add",
            RemoveTooltip = "Remove timer",

            // [C-51] This time tomorrow, on the minute: far enough out to be visible in every
            // format, near enough to be obviously a placeholder.
            Create = () =>
            {
                var now = DateTimeOffset.Now;
                return CountdownTimer.Create(string.Empty, CountdownFormatter.DefaultTarget(now), now);
            },
            Body = timer => TimerBody(timer, save),
        });
    }

    /// <summary>One timer's editor: its name, where it points, and how it reads.</summary>
    private static FrameworkElement TimerBody(CountdownTimer timer, Action save)
    {
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

        var bottom = new Grid
        {
            ColumnSpacing = Fluent.Space2,
            Margin = new Thickness(0, Fluent.Space2, 0, 0),
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

        return new StackPanel { Children = { label, bottom } };
    }
}
