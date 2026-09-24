using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.App.Services.Hearth;
using Themia.Core.Model;

namespace Themia.App.Shell;

/// <summary>
/// Per-widget settings.
/// <para>
/// Edits a <b>clone</b> and only writes it back when the user confirms, so Cancel really cancels.
/// The clone is the dialog's own model rather than a mirror of the live one: mirroring a config
/// into local state and syncing it back is what makes a settings panel fight the thing it edits.
/// </para>
/// </summary>
internal static class WidgetConfigDialog
{
    /// <summary>Returns true when the widget was changed and the caller should persist.</summary>
    public static async Task<bool> EditAsync(XamlRoot xamlRoot, WidgetInstance widget)
    {
        var draft = widget.Config.Clone();
        var label = widget.Label ?? string.Empty;
        var surface = widget.Surface;

        var labelBox = new TextBox
        {
            Text = label,
            MinWidth = 180,
            PlaceholderText = WidgetCatalogLabel(widget.Kind),
        };

        labelBox.TextChanged += (_, _) => label = labelBox.Text;

        var body = new StackPanel { Spacing = 4, MinWidth = 440 };

        // Each kind contributes only its own rows; everything below is shared.
        switch (draft)
        {
            case ClockConfig clock:
                BuildClock(body, clock);
                break;

            case DiskConfig disk:
                BuildDisk(body, disk);
                break;

            case BatteryConfig battery:
                BuildBattery(body, battery);
                break;

            case GaugeConfig gauge:
                BuildGauge(body, gauge);
                break;

            case EmailConfig email:
                BuildEmail(body, email);
                break;

            default:
                break;
        }

        body.Children.Add(FluentLayout.Header("Appearance"));

        body.Children.Add(FluentLayout.Row("", "Background", "How this widget's card is filled",
            FluentLayout.Choice(
                [
                    (WidgetSurface.Card, "Card"),
                    (WidgetSurface.Translucent, "Translucent"),
                    (WidgetSurface.Transparent, "None"),
                ],
                surface,
                value => surface = value)));

        body.Children.Add(FluentLayout.Row("", "Title", "Shown in menus; leave blank for the default", labelBox));

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Widget settings",
            Content = new ScrollViewer { Content = body, MaxHeight = 460 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        widget.Config = draft;
        widget.Surface = surface;
        widget.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        return true;
    }

    private static string WidgetCatalogLabel(WidgetKind kind) => WidgetCatalog.Definition(kind).Label;

    private static void BuildClock(Panel body, ClockConfig clock)
    {
        body.Children.Add(FluentLayout.Header("Clock"));

        body.Children.Add(FluentLayout.Row("", "Face", "Digital readout or a sweeping analog dial",
            FluentLayout.Choice(
                [(ClockFace.Digital, "Digital"), (ClockFace.Analog, "Analog")],
                clock.Face,
                value => clock.Face = value)));

        body.Children.Add(FluentLayout.Row("", "24-hour time", null,
            FluentLayout.Toggle(clock.Use24Hour, value => clock.Use24Hour = value)));

        body.Children.Add(FluentLayout.Row("", "Show seconds",
            "Wakes the app once a second instead of once a minute",
            FluentLayout.Toggle(clock.ShowSeconds, value => clock.ShowSeconds = value)));

        body.Children.Add(FluentLayout.Row("", "Show date", null,
            FluentLayout.Toggle(clock.ShowDate, value => clock.ShowDate = value)));
    }

    private static void BuildGauge(Panel body, GaugeConfig gauge)
    {
        body.Children.Add(FluentLayout.Header("Meter"));
        AddGraphToggle(body, gauge);
    }

    private static void AddGraphToggle(Panel body, GaugeConfig gauge)
        => body.Children.Add(FluentLayout.Row("", "Show history",
            "Draw recent samples behind the readout",
            FluentLayout.Toggle(gauge.ShowGraph, value => gauge.ShowGraph = value)));

    private static void BuildDisk(Panel body, DiskConfig disk)
    {
        body.Children.Add(FluentLayout.Header("Disk"));

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
            }
        };

        body.Children.Add(FluentLayout.Row("", "Drive", "Which volume to report on", combo));
        AddGraphToggle(body, disk);
    }

    private static void BuildBattery(Panel body, BatteryConfig battery)
    {
        body.Children.Add(FluentLayout.Header("Battery"));

        body.Children.Add(FluentLayout.Row("", "Show time remaining",
            "Windows only estimates this while on battery",
            FluentLayout.Toggle(battery.ShowTimeRemaining, value => battery.ShowTimeRemaining = value)));
    }

    private static void BuildEmail(Panel body, EmailConfig email)
    {
        body.Children.Add(FluentLayout.Header("Mail"));

        var mail = AppServices.Mail;
        var collections = mail?.MailCollections ?? [];

        var combo = new ComboBox { MinWidth = 220 };
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

        body.Children.Add(FluentLayout.Row("", "Mailbox", description, combo));

        body.Children.Add(FluentLayout.Row("", "Unread only", null,
            FluentLayout.Toggle(email.UnreadOnly, value => email.UnreadOnly = value)));

        body.Children.Add(FluentLayout.Row("", "Messages", null,
            FluentLayout.Slider(email.EffectiveMaxCount, 1, 50, 1, string.Empty,
                value => email.MaxCount = (int)Math.Round(value))));

        body.Children.Add(FluentLayout.Row("", "Show preview", "The first line of the message",
            FluentLayout.Toggle(email.ShowPreview, value => email.ShowPreview = value)));
    }
}
