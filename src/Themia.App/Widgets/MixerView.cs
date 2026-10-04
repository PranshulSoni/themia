using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>
/// [S-440] Per-application volume, as the Windows volume mixer lays it out: the device's master level
/// on top, then one row per app with its icon, its name, a slider, a live meter and a mute button.
/// <para>
/// [S-1227]..[S-1229] A ladder, not one layout scaled: rows are compact (icon, percent, mute), two
/// lines or one line by width, and the list takes the whole rows that fit — the rest scroll, never
/// half a row. Room left over goes to the device line (which device, how many apps are playing, and
/// the device picker), then to the quiet apps only-active left out; the largest card adds a side
/// panel with the device's own level, the microphone and every output device.
/// </para>
/// <para>
/// The rows are reused by position, never rebuilt. Each snapshot produces fresh row data, and
/// replacing the row visuals would destroy the slider under the user's pointer mid-drag — the
/// original hit exactly that and had to switch its list to position keying for the same reason.
/// </para>
/// </summary>
internal sealed partial class MixerView : Grid, IWidgetView, IWidgetSizeAware, IWidgetSurfaceAware, IWidgetKeys, IWidgetContextActions
{
    /// <summary>[S-1227] Row heights per style: fixed, so the whole-row arithmetic is exact.</summary>
    private const double CompactRowHeight = Fluent.RowHeightCompact;

    private const double TwoLineRowHeight = 48;
    private const double OneLineRowHeight = Fluent.RowHeight;

    /// <summary>[S-1228] The device line's height.</summary>
    private const double DeviceLineHeight = Fluent.RowHeightCompact;

    /// <summary>[S-1229] The side panel's share of an extra-large card, and its bounds.</summary>
    private const double PanelShare = 0.4;

    private const double PanelMaxWidth = 320;

    private readonly AudioFeed _feed;
    private readonly Grid _listColumn = new();

    /// <summary>[S-1624] Under a short list beside the panel: where the other apps will appear, and Windows' own mixer.</summary>
    private readonly StackPanel _footer;
    private readonly Button _device = new();
    private readonly TextBlock _deviceName = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly TextBlock _playing = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText = new();
    private readonly List<RowVisual> _rows = [];
    private readonly SidePanel _panel;

    private MixerConfig _config = new();
    private IWidgetSurface? _surface;
    private WidgetArea _area;
    private AudioSnapshot? _snapshot;
    private AudioPeaks _peaks = AudioPeaks.Silent;
    private Dictionary<string, double> _overrides = new(StringComparer.Ordinal);

    /// <summary>The rows on screen, in order: the list's, then the quiet ones.</summary>
    private List<MixerRow> _shown = [];

    /// <summary>[S-1225] The keyboard cursor's row, by key, so it follows its row across snapshots.</summary>
    private string? _focused;

    private bool _peaksOn;
    private bool _panelOn;
    private bool _loaded;
    private bool _detached;

    public MixerView()
    {
        _feed = new AudioFeed(OnSnapshot, OnPeaks);

        _scroller = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Top,
        };

        BuildDeviceLine();

        _listColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _listColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(_scroller, 1);
        _listColumn.Children.Add(_device);
        _listColumn.Children.Add(_scroller);

        _listColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        _footer = BuildFooter();
        SetRow(_footer, 2);
        _listColumn.Children.Add(_footer);

        _panel = new SidePanel(this);
        _panel.Root.Visibility = Visibility.Collapsed;
        SetColumn(_panel.Root, 1);

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _empty = Fluent.EmptyState(Glyphs.Mixer, _emptyText, out _);
        _empty.Visibility = Visibility.Collapsed;
        SetColumnSpan(_empty, 2);

        Children.Add(_listColumn);
        Children.Add(_panel.Root);
        Children.Add(_empty);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;
        _feed.Preview = surface.IsPreview;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as MixerConfig ?? new MixerConfig();
        Lay();
    }

    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions => _config.DeviceId is null
        ? []
        : [new WidgetAction(Glyphs.Speakers, Str.WidgetsB_Mixer_FollowDefault.Text(), () => PickDevice(null))];

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        Lay();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        // [S-1223] Off screen: stop the meters (the worker stops sampling when no one wants them).
        _loaded = false;
        Lay();
    }

    private void OnSnapshot(AudioSnapshot snapshot)
    {
        if (_detached)
        {
            return;
        }

        _snapshot = snapshot;
        Lay();
    }

    private void OnPeaks(AudioPeaks peaks)
    {
        if (_detached)
        {
            return;
        }

        _peaks = peaks;

        if (!_peaksOn)
        {
            return;
        }

        foreach (var row in _rows)
        {
            row.ShowPeak(peaks);
        }

        _panel.ShowPeaks(peaks);
    }

    /// <summary>
    /// [S-1227]..[S-1229] The ladder: everything the mixer shows, decided for the area it has and the
    /// rows the snapshot holds. Runs on a new area, a new config and a new snapshot.
    /// </summary>
    private void Lay()
    {
        if (_detached)
        {
            return;
        }

        // [S-1229] The side panel: only an extra-large card, where the list cannot use the width.
        var panel = _area.IsMeasured && _area.WidthClass >= SizeClass.Huge && _area.HeightClass >= SizeClass.Expanded;
        var panelWidth = panel ? Math.Min(PanelMaxWidth, Math.Round(_area.Width * PanelShare)) : 0;
        var listWidth = _area.Width - panelWidth;
        var style = MixerRules.RowStyle(listWidth);

        // [S-1223] Meters only beside a slider, and only on a card with room for one per row.
        var peaks = _config.ShowPeaks && _loaded && style != MixerRowStyle.Compact
            && (style == MixerRowStyle.OneLine || _area.Least >= SizeClass.Regular);

        _peaksOn = peaks;
        _panelOn = panel;

        _feed.Want(new AudioWant(_config.DeviceId, Sessions: true, Peaks: peaks, Microphone: panel && _config.ShowMicrophone));

        if (_snapshot is not { } snapshot || !_area.IsMeasured)
        {
            return;
        }

        if (snapshot.Status != AudioStatus.Ready)
        {
            // [S-448] [S-1221] An intentional empty state, not a blank card.
            ShowEmpty(snapshot.Status switch
            {
                AudioStatus.DeviceMissing => Str.WidgetsB_Mixer_DeviceMissing.Format(("device", AudioRules.DeviceName(_config.DeviceId, snapshot.Devices, _config.DeviceName))),
                AudioStatus.NoDevice => Str.WidgetsB_Mixer_NoDevice.Text(),
                _ => Str.WidgetsB_Mixer_Unavailable.Text(),
            });
            return;
        }

        var options = new MixerRowOptions(_config.ShowMaster && !panel, _config.ShowSystemSounds, _config.OnlyActive);
        var grouped = MixerRules.Group(snapshot.Sessions, _config.GroupByApp);
        var main = MixerRules.BuildRows(grouped, snapshot.Master, options);
        var quiet = MixerRules.QuietRows(grouped, options);

        // [S-446] Keep each dragged level until a snapshot confirms it or its row is gone.
        var all = new List<MixerRow>(main.Count + quiet.Count + 1);
        all.AddRange(main);
        all.AddRange(quiet);
        if (snapshot.Master is { } master)
        {
            all.Add(new MixerRow(MixerRules.MasterKey, string.Empty, null, master.Level, master.Muted, true, false, 0));
        }

        if (snapshot.Microphone is { } mic)
        {
            all.Add(new MixerRow(MixerRules.MicrophoneKey, mic.Name, null, mic.Level, mic.Muted, true, false, 0));
        }

        _overrides = MixerRules.ReconcileOverrides(_overrides, all);
        main = MixerRules.ApplyOverrides(main, _overrides);
        quiet = MixerRules.ApplyOverrides(quiet, _overrides);

        if (main.Count == 0 && !panel)
        {
            ShowEmpty(Str.Mixer_Empty.Text());
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _listColumn.Visibility = Visibility.Visible;

        // ---- the height ladder ----------------------------------------------------------------
        var rowHeight = style switch
        {
            MixerRowStyle.Compact => CompactRowHeight,
            MixerRowStyle.TwoLine => TwoLineRowHeight,
            _ => OneLineRowHeight,
        };

        var room = new Disclosure(listWidth, _area.Height - Fluent.Space1);

        // A tall card always names its device first; a shorter one only when every row still fits.
        var device = !panel && _area.HeightClass >= SizeClass.Expanded && room.Take(DeviceLineHeight);
        var rows = Math.Max(1, room.TakeRows(rowHeight, main.Count));

        if (!device && !panel && rows == main.Count && _area.HeightClass >= SizeClass.Regular)
        {
            device = room.Take(DeviceLineHeight);
        }

        // [S-1228] The quiet apps only when the playing ones all fit: they never push one off.
        var quietRows = rows == main.Count ? room.TakeRows(rowHeight, quiet.Count) : 0;

        _shown = [.. main, .. quiet.Take(quietRows)];

        // [S-1624] Beside the panel, a list that all fits shares the column's height rather than
        // stopping short; rows tall enough say what each app is doing under its name.
        var baseRowHeight = rowHeight;

        if (panel && style == MixerRowStyle.OneLine && rows == main.Count && quietRows == quiet.Count)
        {
            rowHeight = MixerFill.RowHeight(_area.Height - Fluent.Space1, _shown.Count, rowHeight);
        }

        ShowDeviceLine(device, snapshot, main);
        ShowRows(style, rowHeight, main.Count);
        _scroller.Height = Math.Min(rows, main.Count) * rowHeight + (quietRows * rowHeight);

        // [S-1624] What a short list leaves (beside the panel, after the rows were shared out, or
        // under a tall card's list) is not left blank: it says where the other apps will appear and
        // offers Windows' own volume mixer.
        var footer = room.Remaining - ((rowHeight - baseRowHeight) * _shown.Count) >= FooterMinHeight;
        _footer.Visibility = footer ? Visibility.Visible : Visibility.Collapsed;
        _listColumn.RowDefinitions[1].Height = footer ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _listColumn.RowDefinitions[2].Height = footer ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        _panel.Root.Width = panelWidth;
        _panel.Root.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;

        if (panel)
        {
            _panel.Show(snapshot, _config, _overrides, _area.Height);
        }

        RestoreFocus();
    }

    /// <summary>The room the footer needs before it is shown: its glyph, two lines and the link.</summary>
    private const double FooterMinHeight = 128;

    private StackPanel BuildFooter()
    {
        var text = new TextBlock { Text = Str.WidgetsB_Mixer_FooterHint.Text() };
        var hint = Fluent.EmptyState(Glyphs.Speakers, text, out _);

        var link = new HyperlinkButton
        {
            // Wrapped rather than clipped: in the longer languages the link is wider than a tall, narrow card.
            Content = new TextBlock { Text = Str.WidgetsB_Mixer_WindowsMixer.Text(), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, Fluent.Space1, 0, 0),
        };
        link.Click += (_, _) =>
        {
            if (_surface is { IsPreview: false })
            {
                ShellLauncher.OpenWindowsSettings(new Uri("ms-settings:apps-volume"));
            }
        };
        return new StackPanel
        {
            Children = { hint, link },
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
    }

    private void ShowEmpty(string message)
    {
        _emptyText.Text = message;
        _empty.Visibility = Visibility.Visible;
        _listColumn.Visibility = Visibility.Collapsed;
        _panel.Root.Visibility = Visibility.Collapsed;
        _shown = [];
    }

    private void ShowRows(MixerRowStyle style, double rowHeight, int mainCount)
    {
        while (_rows.Count < _shown.Count)
        {
            var visual = new RowVisual(this);
            _rows.Add(visual);
            _list.Children.Add(visual.Host);
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            if (i < _shown.Count)
            {
                var row = _shown[i];
                var visual = _rows[i];
                visual.Host.Visibility = Visibility.Visible;
                visual.Arrange(style, rowHeight, _config.ShowIcons, detail: rowHeight >= MixerFill.DetailRowHeight);

                // [S-1228] An app holding a session but silent reads as such, and the quiet rows
                // under the list always do.
                visual.Show(row, _config.ShowIcons, dimmed: i >= mainCount || (!row.Active && !row.IsMaster && !row.IsSystem), _peaksOn);
                visual.ShowPeak(_peaksOn ? _peaks : AudioPeaks.Silent);
            }
            else
            {
                _rows[i].Host.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ---- the device line ([S-1228]) -----------------------------------------------------------

    private void BuildDeviceLine()
    {
        var line = new Grid { ColumnSpacing = Fluent.Space2 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = Fluent.Icon(Glyphs.Speakers, "TextFillColorSecondaryBrush", Fluent.IconSize - 2);
        var chevron = Fluent.Icon(Glyphs.ChevronDown, "TextFillColorTertiaryBrush", Fluent.IconSize - 6);

        _deviceName.TextTrimming = TextTrimming.CharacterEllipsis;
        _deviceName.VerticalAlignment = VerticalAlignment.Center;
        _playing.VerticalAlignment = VerticalAlignment.Center;
        chevron.VerticalAlignment = VerticalAlignment.Center;

        SetColumn(_deviceName, 1);
        SetColumn(_playing, 2);
        SetColumn(chevron, 3);
        line.Children.Add(glyph);
        line.Children.Add(_deviceName);
        line.Children.Add(_playing);
        line.Children.Add(chevron);

        _device.Content = line;
        _device.Height = DeviceLineHeight;
        _device.HorizontalAlignment = HorizontalAlignment.Stretch;
        _device.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _device.Padding = new Thickness(Fluent.Space4, 0, Fluent.Space3, 0);
        _device.Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
        _device.BorderThickness = new Thickness(0);
        _device.CornerRadius = new CornerRadius(0);
        _device.Click += OnDeviceClicked;
        ToolTipService.SetToolTip(_device, Str.WidgetsB_Mixer_ChooseDevice.Text());
    }

    private void ShowDeviceLine(bool shown, AudioSnapshot snapshot, IReadOnlyList<MixerRow> rows)
    {
        _device.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        if (!shown)
        {
            return;
        }

        _deviceName.Text = snapshot.Output?.Name is { Length: > 0 } name ? name : Str.WidgetsB_Mixer_OutputDevice.Text();

        var playing = rows.Count(static row => row.Active && !row.IsMaster && !row.IsSystem);
        _playing.Text = playing == 0 ? Str.WidgetsB_Mixer_Silent.Text() : Str.WidgetsB_Mixer_PlayingCount.Plural(playing);
    }

    private void OnDeviceClicked(object sender, RoutedEventArgs args) => OpenDeviceMenu(_device);

    /// <summary>[S-1222] The device picker. Inert in a preview, like every other control.</summary>
    private void OpenDeviceMenu(FrameworkElement anchor)
    {
        if (_snapshot is not { } snapshot || _surface is not { IsPreview: false })
        {
            return;
        }

        AudioDeviceMenu.Build(snapshot, _config.DeviceId, PickDevice).ShowAt(anchor);
    }

    /// <summary>
    /// [S-1614] A pick makes the device Windows' default and the mixer keeps following the default;
    /// when Windows refuses, the mixer binds to the device and a notice says why ([S-1616], [S-880]).
    /// Null picks "Follow the default output".
    /// </summary>
    private async void PickDevice(AudioDevice? device)
    {
        if (_surface is not { IsPreview: false } surface)
        {
            return;
        }

        try
        {
            if (await _feed.PickAsync(device, _config.DeviceId, _config.DeviceName, _snapshot?.DefaultId) is not { } after || _detached)
            {
                return;
            }

            _config.DeviceId = after.DeviceId;
            _config.DeviceName = after.DeviceName;
            _overrides.Clear();
            surface.RequestSave();
            Lay();

            if (after.Notice is { } notice)
            {
                // [S-1616]/[S-880] Said once, as a notice: the widget is bound now, nothing to answer.
                Themia.App.Services.Notices.Warning(Str.WidgetsB_Mixer_DefaultNotChanged.Text(), notice);
            }
        }
        catch (Exception ex)
        {
            Themia.Core.Diagnostics.Log.Error("audio", "the mixer could not change its device", ex);
        }
    }

    // ---- writes -------------------------------------------------------------------------------

    /// <summary>[S-446] The user moved a slider: hold the value on screen and queue the write.</summary>
    private void OnLevel(MixerRow row, double level)
    {
        _overrides[row.Key] = level;

        // [S-447] A muted row dragged above zero unmutes, as the volume widget and the flyout do —
        // a slider that moves but stays silent reads as broken.
        if (row.Muted && level > 0)
        {
            _feed.SetMute(row.Key, false);
        }

        _feed.SetLevel(row.Key, level);
    }

    private void OnMute(MixerRow row) => _feed.SetMute(row.Key, !row.Muted);

    private void OnReset(MixerRow row)
    {
        _overrides[row.Key] = 1;

        if (row.Muted)
        {
            _feed.SetMute(row.Key, false);
        }

        _feed.SetLevel(row.Key, 1);
    }

    // ---- keys ([S-1225]) ------------------------------------------------------------------------

    public bool HandleKey(WidgetKey key)
    {
        var index = _focused is { } id ? _shown.FindIndex(row => row.Key == id) : -1;
        var action = AudioRules.MixerKey(key, index >= 0 ? index : null, _shown.Count);

        switch (action.Kind)
        {
            case AudioKeyKind.Clear:
                var had = _focused is not null;
                _focused = null;
                return had;

            case AudioKeyKind.Move:
                Focus(action.Index);
                return true;

            case AudioKeyKind.Step:
                var row = _shown[action.Index];
                var level = AudioRules.Step(row.Muted ? 0 : row.Level, action.Delta);
                OnLevel(row, level);
                _rows[action.Index].Show(row with { Level = level, Muted = row.Muted && level <= 0 }, _config.ShowIcons, _rows[action.Index].Dimmed, _peaksOn);
                return true;

            case AudioKeyKind.ToggleMute:
                OnMute(_shown[action.Index]);
                return true;

            case AudioKeyKind.Menu:
                _rows[action.Index].OpenMenu(null);
                return true;

            default:
                return false;
        }
    }

    private void Focus(int index)
    {
        if (index < 0 || index >= _shown.Count || index >= _rows.Count)
        {
            return;
        }

        _focused = _shown[index].Key;
        var host = _rows[index].Host;
        host.Focus(FocusState.Keyboard);
        host.StartBringIntoView();
    }

    /// <summary>After a relayout the cursor goes back to its row; a row that left lets it go.</summary>
    private void RestoreFocus()
    {
        if (_focused is not { } id)
        {
            return;
        }

        var index = _shown.FindIndex(row => row.Key == id);
        if (index < 0)
        {
            _focused = null;
            return;
        }

        if (index < _rows.Count && _rows[index].Host.FocusState == FocusState.Unfocused)
        {
            _rows[index].Host.Focus(FocusState.Keyboard);
        }
    }

    public void Detach()
    {
        _detached = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _device.Click -= OnDeviceClicked;
        _feed.Dispose();
        _panel.Detach();

        foreach (var row in _rows)
        {
            row.Detach();
        }

        _rows.Clear();
        _list.Children.Clear();
        _focused = null;
    }

    private static string RowName(MixerRow row) => row.IsMaster ? Str.WidgetsB_Mixer_Master.Text()
        : row.IsSystem ? Str.Mixer_SystemSounds.Text()
        : row.Key == MixerRules.MicrophoneKey ? (row.Name.Length > 0 ? row.Name : Str.WidgetsB_Mixer_Microphone.Text())
        : row.Name.Length > 0 ? row.Name
        : Str.Mixer_UnknownApp.Text();

    private static Brush Accent() => FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
}
