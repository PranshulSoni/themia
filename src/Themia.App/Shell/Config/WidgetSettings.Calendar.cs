using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services.Accounts;
using Themia.App.Services.Office;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Office;
using Themia.Core.Widgets;
using Themia.App.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-561] The calendar widget's settings: accounts, and under each one its calendars — shown or
/// hidden, and a colour each ([S-566], [S-567]) — then the view, the week start, the agenda length
/// and birthdays. The classic widget's Source and Display tabs, on one page.
/// </summary>
internal static partial class WidgetSettings
{
    private const string CalendarScope = "calendar-settings";

    public static void Calendar(List<FrameworkElement> rows, CalendarConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        // Every calendar picker on the page, so a change that moves the palette indices (a calendar
        // shown or hidden, an account chosen) can redraw the "Automatic" colours of all of them.
        var pickers = new List<Action>();
        void RedrawPickers()
        {
            foreach (var redraw in pickers)
            {
                redraw();
            }
        }

        void SaveAndRedraw()
        {
            save();
            RedrawPickers();
        }

        rows.Add(FluentLayout.Header("Accounts and calendars"));
        OfficeAccountRows(rows, AccountFeatures.Calendar, Glyphs.Calendar, "calendar",
            "Sign in to Microsoft, Google or a CalDAV calendar on the Accounts page",
            () => config.AccountIds, ids => config.AccountIds = ids, SaveAndRedraw,
            account => CalendarPicker(account, config, SaveAndRedraw, pickers));

        rows.Add(FluentLayout.Header("Display"));

        rows.Add(FluentLayout.Row(Glyphs.View, "View",
            "A small widget shows the agenda when the view would not fit",
            FluentLayout.Choice<CalendarViewMode>(
                [(CalendarViewMode.Month, "Month"), (CalendarViewMode.Week, "Week"), (CalendarViewMode.Day, "Day"), (CalendarViewMode.Agenda, "Agenda")],
                config.EffectiveView,
                value => { config.View = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Week starts on", null,
            FluentLayout.Choice<WeekStartDay>(
                [(WeekStartDay.Monday, "Monday"), (WeekStartDay.Sunday, "Sunday")],
                config.EffectiveWeekStart,
                value => { config.WeekStart = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Agenda entries",
            "How many the agenda lists, across the next 30 days",
            FluentLayout.Slider(config.EffectiveAgendaCount, 1, 100, 1, string.Empty,
                value => { config.AgendaCount = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Contact, "Show birthdays",
            "Your contacts' birthdays, from Microsoft and Google accounts",
            FluentLayout.Toggle(config.ShowBirthdays, value => { config.ShowBirthdays = value; save(); })));
    }

    /// <summary>
    /// [S-567] One account's calendars: each with a colour, and — when there is more than one — a
    /// switch. The default calendar starts on; turning the last one off goes back to the default.
    /// Filled when the list arrives; the account's own default works meanwhile.
    /// </summary>
    private static FrameworkElement CalendarPicker(AccountInfo account, CalendarConfig config, Action save, List<Action> pickers)
    {
        var host = new StackPanel { Spacing = Fluent.Space1, Margin = new Thickness(Fluent.Space6, 0, 0, Fluent.Space2) };
        IReadOnlyList<CalendarInfo>? calendars = null;

        var loading = FluentLayout.Note("Looking for this account's calendars…");
        host.Children.Add(loading);

        void Redraw()
        {
            host.Children.Clear();

            if (calendars is null)
            {
                host.Children.Add(loading);
                return;
            }

            var selected = CalendarRules.SelectAccounts(config.AccountIds, OfficeSources.Current?.Accounts(AccountFeatures.Calendar).Select(a => a.Id).ToList() ?? []);
            var targets = CalendarRules.Targets(selected, config.Calendars);

            foreach (var calendar in calendars)
            {
                // The palette index is the calendar's place among everything the widget fetches,
                // exactly as the widget resolves it, so the swatch here is the chip there.
                var index = -1;
                for (var i = 0; i < targets.Count; i++)
                {
                    if (targets[i].AccountId == account.Id
                        && (targets[i].CalendarId == calendar.Id || (targets[i].CalendarId is null && calendar.IsDefault)))
                    {
                        index = i;
                        break;
                    }
                }

                var key = index >= 0 ? CalendarRules.ColorKey(account.Id, targets[index].CalendarId) : calendar.Id;
                var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space3 };

                if (index >= 0)
                {
                    controls.Children.Add(ColorChoice(config, key, index, save));
                }

                if (calendars.Count > 1)
                {
                    var calendarId = calendar.Id;
                    controls.Children.Add(FluentLayout.Toggle(CalendarRules.IsCalendarShown(config.Calendars, account.Id, calendar), _ =>
                    {
                        config.Calendars = CalendarRules.ToggleCalendar(config.Calendars, account.Id, calendarId, calendars);
                        save();
                    }));
                }

                host.Children.Add(FluentLayout.Row(Glyphs.Calendar, calendar.Name, calendar.IsDefault ? "Default calendar" : null, controls));
            }
        }

        pickers.Add(Redraw);
        _ = LoadCalendarsAsync();
        return host;

        async Task LoadCalendarsAsync()
        {
            if (OfficeSources.Current is not { } source)
            {
                return;
            }

            try
            {
                var result = await source.CalendarsAsync(account.Id, force: false, CancellationToken.None).ConfigureAwait(true);
                calendars = result.Value ?? [];

                if (result.Value is null)
                {
                    loading.Text = $"The calendars could not be listed: {SyncProblems.Message(result.Problem, "calendar")} The default calendar is shown.";
                    host.Children.Clear();
                    host.Children.Add(loading);
                    return;
                }

                Redraw();
            }
            catch (Exception ex)
            {
                // Started from building the page: nothing above may see an exception.
                Log.Error(CalendarScope, $"could not list the calendars of {account.Id}", ex);
                loading.Text = "The calendars could not be listed. The default calendar is shown.";
            }
        }
    }

    /// <summary>[S-566] Automatic (the palette colour at the calendar's index) or one of the six, by name.</summary>
    private static ComboBox ColorChoice(CalendarConfig config, string key, int index, Action save)
    {
        var combo = new ComboBox { MinWidth = 150 };
        var automatic = CalendarRules.PaletteColor(index);

        combo.Items.Add(new ComboBoxItem { Content = Swatch(automatic, "Automatic"), Tag = string.Empty });
        for (var i = 0; i < CalendarRules.Palette.Count; i++)
        {
            combo.Items.Add(new ComboBoxItem { Content = Swatch(CalendarRules.Palette[i], CalendarRules.PaletteNames[i]), Tag = CalendarRules.Palette[i] });
        }

        var chosen = config.CalendarColors.TryGetValue(key, out var hex) && CalendarRules.IsHexColor(hex) ? hex.ToLowerInvariant() : string.Empty;
        var selected = 0;
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem { Tag: string tag } && tag == chosen)
            {
                selected = i;
            }
        }

        // A colour picked elsewhere (by hand in the file) is kept and shown as its own entry.
        if (chosen.Length > 0 && selected == 0)
        {
            combo.Items.Add(new ComboBoxItem { Content = Swatch(chosen, chosen), Tag = chosen });
            selected = combo.Items.Count - 1;
        }

        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string tag })
            {
                return;
            }

            var colors = new Dictionary<string, string>(config.CalendarColors, StringComparer.Ordinal);
            if (tag.Length == 0)
            {
                colors.Remove(key);
            }
            else
            {
                colors[key] = tag;
            }

            config.CalendarColors = colors;
            save();
        };

        return combo;
    }

    private static StackPanel Swatch(string hex, string label)
    {
        HexColor.TryParse(hex, out var r, out var g, out var b);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children =
            {
                new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b)), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }
}
