using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-1569] The detail panel: on a large tile whose list is short (one headset on an L or XL tile),
/// the height under the rows shows one device in full — its icon and name large, what it is and
/// whether it is connected, then its battery with a level bar, its signal and its address. The
/// device is the one last clicked in the list, else the first (connected devices come first).
/// </summary>
internal sealed partial class BluetoothView
{
    /// <summary>The battery bar in the panel: long enough to read a level at a glance.</summary>
    private const double PanelBarWidth = 120;

    private readonly Border _detail = new()
    {
        VerticalAlignment = VerticalAlignment.Top,
        BorderThickness = new Thickness(0, Fluent.Hairline, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>The device picked for the panel, by id; null until a row is clicked.</summary>
    private string? _selectedId;

    /// <summary>The device the panel is showing, by id, so its row is lit.</summary>
    private string? _detailId;

    /// <summary>Put the panel under the rows, or hide it. Runs after the rows were built.</summary>
    private void ShowDetail(BluetoothRowPlan plan, IReadOnlyList<BluetoothDeviceEntry> devices, double listHeight)
    {
        if (!plan.Detail || BluetoothRules.Selected(devices, _selectedId) is not { } device)
        {
            _detail.Visibility = Visibility.Collapsed;
            _detail.Child = null;
            return;
        }

        var inset = WidgetLayout.ListInset;
        var rows = devices.Count * plan.RowHeight;
        var top = inset.Top + rows + Fluent.Space2;
        var huge = _area.Least >= SizeClass.Huge;

        _detail.BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray);
        _detail.Margin = new Thickness(Fluent.Space4, top, Fluent.Space4, 0);
        _detail.Height = Math.Max(0, listHeight + inset.Top - top);
        _detail.Child = Panel(device, huge, _area.WidthClass < SizeClass.Expanded);
        _detail.Visibility = Visibility.Visible;
    }

    private StackPanel Panel(BluetoothDeviceEntry device, bool huge, bool narrow)
    {
        var icon = Fluent.Icon(
            KindGlyphs[(int)device.Kind],
            device.Connected ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush",
            huge ? 48 : narrow ? 28 : 36);
        icon.VerticalAlignment = VerticalAlignment.Center;

        var name = Fluent.Text(huge ? FluentType.TitleLarge : narrow ? FluentType.Subtitle : FluentType.Title);
        name.Text = device.Name;
        // A narrow column lets a long name take a second line rather than lose its end.
        name.MaxLines = narrow ? 2 : 1;
        name.TextWrapping = narrow ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;

        var what = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        what.Text = $"{BluetoothRules.KindLabel(device.Kind)} · {BluetoothRules.ConnectionLabel(device.Connected)}";

        var title = new Grid
        {
            ColumnSpacing = narrow ? Fluent.Space3 : Fluent.Space4,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, what } };
        SetColumn(lines, 1);
        title.Children.Add(icon);
        title.Children.Add(lines);

        // A narrow column stacks the facts; a wide tile lines them up.
        var facts = new StackPanel
        {
            Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = narrow ? Fluent.Space3 : Fluent.Space6 + Fluent.Space2,
        };

        // The battery is always named: "Not reported" says the device was asked, which a missing
        // figure does not. The signal only while its scan is on.
        if (_config.ShowBattery)
        {
            facts.Children.Add(BluetoothRules.BatteryLabel(device.BatteryPercent) is { } battery && device.BatteryPercent is { } percent
                ? Fact("Battery", battery, huge, LevelBar(percent))
                : Fact("Battery", "Not reported", huge, null, quiet: true));
        }

        if (_config.ShowSignal)
        {
            facts.Children.Add(BluetoothRules.SignalLabel(device.SignalDbm) is { } signal
                ? Fact("Signal", signal, huge, null)
                : Fact("Signal", "Not heard", huge, null, quiet: true));
        }

        if (BluetoothRules.AddressText(device.Id) is { } address)
        {
            facts.Children.Add(Fact("Address", address, huge, null));
        }

        var panel = new StackPanel
        {
            Spacing = huge ? Fluent.Space6 : Fluent.Space4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title },
        };

        if (facts.Children.Count > 0)
        {
            panel.Children.Add(facts);
        }

        return panel;
    }

    /// <summary>One labelled figure: the label small and quiet above, the value in tabular figures under it.</summary>
    private static StackPanel Fact(string label, string value, bool huge, FrameworkElement? under, bool quiet = false)
    {
        var caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        caption.Text = label;

        var figure = Fluent.Text(huge ? FluentType.Subtitle : FluentType.BodyStrong, quiet ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        figure.Text = value;
        figure.MaxLines = 1;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(figure, FontNumeralAlignment.Tabular);

        var fact = new StackPanel { Spacing = Fluent.Space1, Children = { caption, figure } };

        if (under is not null)
        {
            fact.Children.Add(under);
        }

        return fact;
    }

    /// <summary>The battery level as a bar, critical at 20% and below like the tray's.</summary>
    private Grid LevelBar(int percent)
    {
        var fill = new Rectangle
        {
            Width = PanelBarWidth * Math.Clamp(percent, 0, 100) / 100.0,
            Height = BarHeight,
            RadiusX = BarHeight / 2,
            RadiusY = BarHeight / 2,
            Fill = percent <= 20 ? _lowBrush : _connectedBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        return new Grid
        {
            Width = PanelBarWidth,
            Height = BarHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(BarHeight / 2),
            Background = _trackBrush,
            Children = { fill },
        };
    }
}
