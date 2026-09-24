using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Hearth;
using Themia.Core.Model;
using Themia.App.Shell;

namespace Themia.App.Shell.Config;

/// <summary>
/// The rows each widget kind contributes to its settings page.
/// <para>
/// Split out from the page so the two things that vary — which rows exist, and where the page puts
/// them — stay independent. Each row writes straight to the live config and then saves, because the
/// widget is on the desktop behind the window and the whole point is to watch it change.
/// </para>
/// </summary>
internal static class WidgetSettings
{
    public static void Clock(List<FrameworkElement> rows, ClockConfig clock, Action save)
    {
        rows.Add(FluentLayout.Header("Clock"));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Face", "Digital readout or a sweeping analog dial",
            FluentLayout.Choice(
                [(ClockFace.Digital, "Digital"), (ClockFace.Analog, "Analog")],
                clock.Face,
                value => { clock.Face = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Hours, "24-hour time", null,
            FluentLayout.Toggle(clock.Use24Hour, value => { clock.Use24Hour = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Show seconds",
            "Wakes the app once a second instead of once a minute",
            FluentLayout.Toggle(clock.ShowSeconds, value => { clock.ShowSeconds = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Show date", null,
            FluentLayout.Toggle(clock.ShowDate, value => { clock.ShowDate = value; save(); })));
    }

    public static void Gauge(List<FrameworkElement> rows, GaugeConfig gauge, Action save)
    {
        rows.Add(FluentLayout.Header("Meter"));
        AddGraphToggle(rows, gauge, save);
    }

    public static void Disk(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        rows.Add(FluentLayout.Header("Disk"));

        var drives = SystemMetrics.FixedDrives();
        var current = SystemMetrics.NormaliseDrive(disk.Drive);

        // A drive that has since been removed would otherwise leave the box blank and the setting
        // silently pointing at nothing.
        if (!drives.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            drives.Insert(0, current);
        }

        var combo = new ComboBox { MinWidth = 150 };
        foreach (var drive in drives)
        {
            combo.Items.Add(new ComboBoxItem { Content = drive, Tag = drive });
        }

        combo.SelectedIndex = Math.Max(0, drives.FindIndex(d => d.Equals(current, StringComparison.OrdinalIgnoreCase)));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string drive })
            {
                disk.Drive = drive;
                save();
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Disk, "Drive", "Which volume to report on", combo));
        AddGraphToggle(rows, disk, save);
    }

    public static void Battery(List<FrameworkElement> rows, BatteryConfig battery, Action save)
    {
        rows.Add(FluentLayout.Header("Battery"));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Show time remaining",
            "Windows only estimates this while on battery",
            FluentLayout.Toggle(battery.ShowTimeRemaining, value => { battery.ShowTimeRemaining = value; save(); })));
    }

    public static void Email(List<FrameworkElement> rows, EmailConfig email, Action save)
    {
        rows.Add(FluentLayout.Header("Mail"));

        var mail = AppServices.Mail;
        var collections = mail?.MailCollections ?? [];

        var combo = new ComboBox { MinWidth = 260 };
        combo.Items.Add(new ComboBoxItem { Content = "All mailboxes", Tag = string.Empty });

        foreach (var collection in collections)
        {
            combo.Items.Add(new ComboBoxItem { Content = collection.Name, Tag = collection.Id });
        }

        var selectedIndex = 0;
        for (var i = 0; i < collections.Count; i++)
        {
            if (collections[i].Id == email.CollectionId)
            {
                selectedIndex = i + 1;
                break;
            }
        }

        combo.SelectedIndex = selectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string id })
            {
                email.CollectionId = id;
                save();
            }
        };

        // Accounts belong to hearth, so this says where the list comes from rather than offering to
        // sign anything in.
        var description = mail?.Status switch
        {
            HearthStatus.Ready when collections.Count > 0 => "Granted by hearth",
            HearthStatus.Ready => "hearth has granted no mailboxes",
            HearthStatus.NotRunning => "hearth is not running",
            HearthStatus.Denied => "hearth has not granted access",
            _ => "Accounts are managed in hearth",
        };

        rows.Add(FluentLayout.Row(Glyphs.Mail, "Mailbox", description, combo));

        rows.Add(FluentLayout.Row(Glyphs.Unread, "Unread only", null,
            FluentLayout.Toggle(email.UnreadOnly, value => { email.UnreadOnly = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Messages", null,
            FluentLayout.Slider(email.EffectiveMaxCount, 1, 50, 1, string.Empty,
                value => { email.MaxCount = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show preview", "The first line of the message",
            FluentLayout.Toggle(email.ShowPreview, value => { email.ShowPreview = value; save(); })));
    }

    private static void AddGraphToggle(List<FrameworkElement> rows, GaugeConfig gauge, Action save)
        => rows.Add(FluentLayout.Row(Glyphs.Motion, "Show history",
            "Draw recent samples behind the readout",
            FluentLayout.Toggle(gauge.ShowGraph, value => { gauge.ShowGraph = value; save(); })));
}
