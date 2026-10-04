using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The paired Bluetooth devices, as a Windows 11 list.
/// <para>
/// Built to sit next to the Bluetooth page in Settings: a 40px list row, the Body step for the
/// device name, a 16px Segoe glyph chosen by what the device is, and no rule between rows. The row
/// carries the 16px inset as its own padding, so the highlight under the pointer reaches both card
/// edges the way a row's does.
/// </para>
/// <para>
/// <b>Read-only.</b> Nothing here connects, disconnects or unpairs. A surface that sits under every
/// other window, with no confirmation and no undo, is the wrong place to drop a headset mid-call.
/// </para>
/// <para>
/// Every empty list says which empty list it is. A machine with no radio, one with the radio off
/// and one with nothing paired are three different facts, and rendering all three as a blank tile
/// throws away the only useful thing the widget knew.
/// </para>
/// </summary>
internal sealed partial class BluetoothView : Grid, IWidgetView, IWidgetHeaderContent
{
    private const string Scope = "bluetooth";

    /// <summary>Below this the connection word is dropped and the dot carries the state alone.</summary>
    private const double StatusWordMinimumWidth = 260;

    /// <summary>The connection dot's diameter. Fluent's smallest meaningful mark.</summary>
    private const double DotSize = Fluent.Space2;

    // Segoe Fluent Icons code points this view needs beyond the shared table.
    private static readonly string[] KindGlyphs =
    [
        From(0xE702), // unknown  - Bluetooth
        From(0xE7F6), // audio    - Headphone
        From(0xE962), // mouse    - Mouse
        From(0xE765), // keyboard - KeyboardClassic
        From(0xE8EA), // phone    - CellPhone
        From(0xE7FC), // gamepad  - Game
        From(0xE929), // pen      - PenWorkspace
        From(0xE916), // watch    - Stopwatch
    ];

    private readonly TextBlock _count = new();
    private readonly Border _badge;
    private readonly ProgressRing _busy = new();
    private readonly StackPanel _trailing;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _message;
    private readonly Border _messageHost;

    // One per row, so the hover handlers come off with the rows they light. A leaked one keeps the
    // row alive and re-lights it after the list has been redrawn.
    private readonly List<IDisposable> _hovers = [];

    private readonly Brush _connectedBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly Brush _pairedBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);

    private Ticker? _ticker;
    private BluetoothConfig _config = new();

    /// <summary>
    /// [S-13a] A copy of the settings this view last acted on, which is what a change is measured
    /// against. Not <c>_config</c>: the settings page edits the live config object in place, so by
    /// the time a change arrives the "previous" config already holds the new values, and comparing
    /// the two found no difference — a new address or place stayed unfetched until the next tick.
    /// </summary>
    private BluetoothConfig _applied = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CancellationTokenSource? _inFlight;

    private BluetoothSnapshot _snapshot = BluetoothSnapshot.Of(BluetoothStatus.Loading);

    /// <summary>
    /// Which enumeration is current. A reply carrying an older number is a radio the widget has
    /// stopped waiting on — a GATT pass that took eight seconds must not overwrite a newer one.
    /// </summary>
    private int _generation;

    private bool _statusWords = true;
    private bool _detached;

    public BluetoothView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _busy.Width = 14;
        _busy.Height = 14;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        _badge = Fluent.Badge(_count);
        _badge.Visibility = Visibility.Collapsed;
        _badge.Margin = new Thickness(0, 0, Fluent.Space2, 0);

        // Both sit in the card header's trailing slot: the ring while a pass is out, the count once
        // one has landed. A panel rather than swapping the slot's child, so neither has to know
        // about the other. Built here because the card reads it once, when it builds the header.
        _trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _badge, _busy },
        };

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = WidgetLayout.ListInset,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        _message = Fluent.Text(FluentType.Body, "TextFillColorTertiaryBrush");
        _message.TextWrapping = TextWrapping.Wrap;
        _message.TextTrimming = TextTrimming.None;
        _message.TextAlignment = TextAlignment.Center;

        _messageHost = new Border
        {
            Child = _message,
            Padding = WidgetLayout.SideInset,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        Children.Add(_scroll);
        Children.Add(_messageHost);

        SizeChanged += OnSizeChanged;
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _trailing;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as BluetoothConfig ?? new BluetoothConfig();
        var previous = _applied;
        _config = next;
        _applied = (BluetoothConfig)next.Clone();

        // Only the one field that changes what comes off the radio is worth a re-enumeration.
        // Everything else redraws what is already in hand: dragging the row-count slider must not
        // fire a GATT pass per frame.
        var needsRead = previous.ShowBattery != next.ShowBattery;

        // Subscribing fires a tick of its own, which reads with the new config already in place.
        if (Resubscribe())
        {
            return;
        }

        if (needsRead)
        {
            Reload();
        }
        else
        {
            Render();
        }
    }

    /// <summary>
    /// Rebuild the ticker subscription only when the cadence actually changed, and say whether it
    /// did — a new subscription ticks immediately, so the caller has nothing left to do.
    /// </summary>
    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

        if (_subscription is not null && interval == _subscribedInterval)
        {
            return false;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Reload);
        return _subscription is not null;
    }

    /// <summary>
    /// Start an enumeration. Never on the UI thread: enumerating the radios is an RPC and a GATT
    /// battery read waits on a peripheral to answer, which on the dispatcher is every widget on the
    /// desktop frozen with it.
    /// </summary>
    private void Reload()
    {
        if (_detached)
        {
            return;
        }

        // The previous pass is now for a configuration nobody is looking at.
        _inFlight?.Cancel();

        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;
        var withBattery = _config.ShowBattery;

        _ = LoadAsync(withBattery, generation, cts);
    }

    private async Task LoadAsync(bool withBattery, int generation, CancellationTokenSource cts)
    {
        try
        {
            SetBusy(true);

            var snapshot = await BluetoothInventory.ReadAsync(withBattery, cts.Token).ConfigureAwait(true);

            // A late reply from a pass the widget has moved on from. Discarded rather than drawn.
            if (_detached || generation != _generation)
            {
                return;
            }

            _snapshot = snapshot;
            Render();
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer pass, which will draw in this one's place.
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the paired-device pass failed", ex);

            if (!_detached && generation == _generation)
            {
                _snapshot = BluetoothSnapshot.Of(BluetoothStatus.Failed);
                Render();
            }
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                _inFlight = null;
                SetBusy(false);
            }

            cts.Dispose();
        }
    }

    private void SetBusy(bool busy)
    {
        if (_detached)
        {
            return;
        }

        _busy.IsActive = busy;
        _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var wanted = args.NewSize.Width >= StatusWordMinimumWidth;

        if (wanted == _statusWords)
        {
            // The rows still change height at the density breakpoint even when the columns do not.
            ApplyDensity();
            return;
        }

        _statusWords = wanted;
        Render();
    }

    /// <summary>
    /// Draw the snapshot in hand. Every state with no rows says which state it is: a blank tile is
    /// indistinguishable from a broken one, and each of these needs a different thing from whoever
    /// is reading it.
    /// </summary>
    private void Render()
    {
        ClearRows();

        List<BluetoothDeviceEntry> devices = _snapshot.Status == BluetoothStatus.Ok
            ? BluetoothRules.Arrange(_snapshot.Devices, _config.ConnectedOnly, _config.EffectiveMaxCount)
            : [];

        var connected = 0;

        foreach (var device in devices)
        {
            if (device.Connected)
            {
                connected++;
            }
        }

        _count.Text = connected.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _badge.Visibility = connected > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (devices.Count == 0)
        {
            // Ok with no rows is "nothing paired", which StatusMessage already words correctly.
            _message.Text = BluetoothRules.StatusMessage(_snapshot.Status);
            _messageHost.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _messageHost.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        foreach (var device in devices)
        {
            _list.Children.Add(BuildRow(device));
        }

        ApplyDensity();
    }

    private FrameworkElement BuildRow(BluetoothDeviceEntry device)
    {
        var icon = Fluent.Icon(
            KindGlyphs[(int)device.Kind],
            device.Connected ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");

        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var name = Fluent.Text(FluentType.Body);
        name.Text = device.Name;

        // The dot is a colour from the theme, never a literal: accent for connected, the disabled
        // text colour for merely paired, both of which follow light and dark.
        var dot = new Ellipse
        {
            Width = DotSize,
            Height = DotSize,
            Fill = device.Connected ? _connectedBrush : _pairedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Fluent.Space2, 0, 0, 0),
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        SetColumn(icon, 0);
        SetColumn(name, 1);
        grid.Children.Add(icon);
        grid.Children.Add(name);

        // Only when the device actually reported one. A column of blanks down the list is worse
        // than no column, and "--" would read as a battery at zero.
        if (_config.ShowBattery && BluetoothRules.BatteryLabel(device.BatteryPercent) is { } battery)
        {
            var level = Detail(battery);
            SetColumn(level, 2);
            grid.Children.Add(level);
        }

        if (_statusWords)
        {
            var state = Detail(BluetoothRules.ConnectionLabel(device.Connected));
            SetColumn(state, 3);
            grid.Children.Add(state);
        }

        SetColumn(dot, 4);
        grid.Children.Add(dot);

        var row = Fluent.Row(grid);
        _hovers.Add(Fluent.Hover(row));

        // The connection state in words for a reader who cannot see the dot, and the battery
        // alongside it, on a surface that has room for neither.
        ToolTipService.SetToolTip(row, BluetoothRules.BatteryLabel(device.BatteryPercent) is { } percent
            ? $"{device.Name} · {BluetoothRules.ConnectionLabel(device.Connected)} · {percent}"
            : $"{device.Name} · {BluetoothRules.ConnectionLabel(device.Connected)}");

        return row;
    }

    /// <summary>
    /// A right-aligned detail column: the secondary text colour, the Caption step, and tabular
    /// figures so the percentages line up down the list instead of jittering per row.
    /// </summary>
    private static TextBlock Detail(string text)
    {
        var block = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        block.Text = text;
        block.TextAlignment = TextAlignment.Right;
        block.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    /// <summary>
    /// Switch the rows between the standard and compact heights. A short tile shows fewer rows at
    /// the right height rather than the same rows squeezed — rows sized to the tile are the single
    /// clearest tell of a web table.
    /// </summary>
    private void ApplyDensity()
    {
        var compact = ActualHeight > 0 && ActualHeight < Fluent.CompactBelow;

        foreach (var child in _list.Children)
        {
            if (child is Border row)
            {
                Fluent.SetDensity(row, compact);
            }
        }
    }

    /// <summary>Drop the rows and, with them, their hover handlers.</summary>
    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
    }

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;
        ClearRows();

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        // Cancel but do not dispose: the in-flight pass owns its source and frees it on the way
        // out, so there is exactly one owner however many times this is called.
        _inFlight?.Cancel();
        _inFlight = null;

        _ticker = null;
    }

    private static string From(int codePoint) => char.ConvertFromUtf32(codePoint);
}
