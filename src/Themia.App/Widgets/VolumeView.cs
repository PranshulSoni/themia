using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>
/// The master level of an output device, as the volume flyout would show it.
/// <para>
/// [S-1220] A readout first, kept current by Core Audio's own volume notification (through the
/// shared <see cref="CoreAudio"/> worker) rather than by a poll, so the tile follows the volume keys,
/// the flyout and whatever a game just did the moment it happens. The slider is the second job, and
/// it is built so it cannot fight the first — while the thumb is held, snapshots leave it alone, and
/// a dragged level is held on screen until Windows confirms it.
/// </para>
/// <para>
/// [S-1230] A ladder: the number and the slider at every size; the device's name in place of
/// "Output level" once there is room beside the number; a live meter from a medium tile; the
/// microphone's own row on a large one; and on the largest, the apps playing on the device and a
/// device switcher on the name.
/// </para>
/// </summary>
internal sealed partial class VolumeView : Grid, IWidgetView, IWidgetSizeAware, IWidgetSurfaceAware, IWidgetKeys, IWidgetContextActions
{
    /// <summary>Below this content width the caption beside the number is dropped: the number is the point.</summary>
    private const double CaptionMinimumWidth = 168;

    /// <summary>[S-1231] How many apps the largest tile lists, and each one's line.</summary>
    private const int TopAppCount = 3;

    /// <summary>[S-1231] On a tile as tall as the largest, twice as many.</summary>
    private const int TallAppCount = 6;

    private const double AppLineHeight = 28;

    private readonly AudioFeed _feed;
    private readonly TextBlock _readout = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly FontIcon _chevron = Fluent.Icon(Glyphs.ChevronDown, "TextFillColorTertiaryBrush", Fluent.IconSize - 6);
    private readonly Button _device = new();
    private readonly Grid _reading = new();
    private readonly StackPanel _apps = new() { Spacing = 0 };
    private readonly TextBlock _appsCaption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly List<AppLine> _appLines = [];
    private readonly StackPanel _micBlock = new();
    private readonly TextBlock _micName = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly LevelRow _mic;
    private readonly Rectangle _meter = new() { Height = LevelRow.MeterHeight };
    private readonly Rectangle _meterTrack = new() { Height = LevelRow.MeterHeight };
    private readonly Grid _meterHost = new();
    private readonly ScaleTransform _meterScale = new() { ScaleX = 0 };
    private readonly Slider _slider = new();
    private readonly Button _mute = new();
    private readonly FontIcon _muteIcon;
    private readonly Grid _band = new();
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText = new() { Text = "No output device" };

    // Looked up once: the readout swaps between them on every change.
    private readonly Brush _readingBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
    private readonly Brush _mutedBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);

    /// <summary>
    /// The exact delegate instances the slider was hooked with. <c>RemoveHandler</c> matches on the
    /// delegate, so building a fresh one at teardown would unhook nothing and leave a torn-down
    /// view listening to a control that outlives it.
    /// </summary>
    private readonly PointerEventHandler _grab;

    private readonly PointerEventHandler _release;

    private VolumeConfig _config = new();
    private IWidgetSurface? _surface;
    private WidgetArea _area;
    private AudioSnapshot? _snapshot;
    private AudioPeaks _peaks = AudioPeaks.Silent;
    private Dictionary<string, double> _overrides = new(StringComparer.Ordinal);
    private TypeStep _heroStep = FluentType.Subtitle;
    private TypeStep _captionStep = FluentType.Caption;
    private bool _peaksOn;
    private bool _switcher;
    private bool _loaded;

    /// <summary>
    /// True while the slider is being written from a snapshot rather than by a person. The slider
    /// raises <c>ValueChanged</c> either way, and without this every snapshot would write the level
    /// it had just read straight back into the mixer.
    /// </summary>
    private bool _echoing;

    /// <summary>
    /// True while the thumb is held. A snapshot that lands mid-drag would snap it back to where the
    /// mixer was a moment ago, which feels like the control fighting the hand on it.
    /// </summary>
    private bool _grabbed;

    private bool _detached;

    public VolumeView()
    {
        _feed = new AudioFeed(OnSnapshot, OnPeaks);

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _readout.Text = "--";
        _readout.Foreground = _readingBrush;
        _readout.VerticalAlignment = VerticalAlignment.Bottom;

        // The caption is the device switcher's face ([S-1222]): plain text until the tile is large
        // enough to offer the switch, then the same text with a chevron.
        _caption.TextTrimming = TextTrimming.CharacterEllipsis;
        _caption.VerticalAlignment = VerticalAlignment.Center;
        _chevron.VerticalAlignment = VerticalAlignment.Center;
        _chevron.Margin = new Thickness(Fluent.Space1, 0, 0, 0);

        var face = new Grid();
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(_chevron, 1);
        face.Children.Add(_caption);
        face.Children.Add(_chevron);

        _device.Content = face;
        _device.Padding = new Thickness(0);
        _device.MinWidth = 0;
        _device.MinHeight = 0;
        _device.Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
        _device.BorderThickness = new Thickness(0);
        _device.HorizontalAlignment = HorizontalAlignment.Left;
        _device.VerticalAlignment = VerticalAlignment.Bottom;

        // Lifted off the baseline it sits beside: bottom-aligning a 12px caption against a 68px
        // number puts it below the number's baseline, which reads as a slipped line.
        _device.Margin = new Thickness(0, 0, 0, Fluent.Space1);
        _device.Click += OnDeviceClicked;

        // A Grid rather than a horizontal StackPanel: a StackPanel measures its children with
        // unbounded width, so the caption never learns how much room it has and its trimming never
        // fires — on a narrow tile it runs off the edge of the card instead of ellipsising.
        _reading.ColumnSpacing = Fluent.Space2;
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _reading.Margin = WidgetLayout.SideInset;
        _reading.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(_device, 1);
        _reading.Children.Add(_readout);
        _reading.Children.Add(_device);

        // [S-1231] The apps playing on the device, read-only.
        _appsCaption.Text = "Apps on this device";
        _apps.Children.Add(_appsCaption);
        _apps.Margin = new Thickness(WidgetLayout.ContentInset, 0, WidgetLayout.ContentInset, Fluent.Space2);
        SetRow(_apps, 1);

        // [S-1224] The microphone row.
        _mic = new LevelRow(LevelRow.MicrophoneGlyph, OnMicLevel, OnMicMute);
        _micName.TextTrimming = TextTrimming.CharacterEllipsis;
        _micBlock.Children.Add(_micName);
        _micBlock.Children.Add(_mic.Root);
        _micBlock.Margin = new Thickness(WidgetLayout.ContentInset, 0, WidgetLayout.ContentInset, Fluent.Space2);
        SetRow(_micBlock, 2);

        // [S-1223] The meter: a scale transform on a full-width bar, so a sample costs no layout.
        _meter.Fill = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        _meter.RenderTransform = _meterScale;
        _meter.RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5);
        _meter.IsHitTestVisible = false;
        _meterTrack.Fill = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);
        _meterHost.Children.Add(_meterTrack);
        _meterHost.Children.Add(_meter);
        SetRow(_meterHost, 3);

        _muteIcon = Fluent.Icon(Glyphs.Volume3, "TextFillColorPrimaryBrush");
        _mute.Content = _muteIcon;
        _mute.Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1);
        _mute.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _mute.VerticalAlignment = VerticalAlignment.Center;
        _mute.IsTabStop = false;
        _mute.Click += OnMuteClicked;
        ToolTipService.SetToolTip(_mute, "Mute");

        _slider.Minimum = 0;
        _slider.Maximum = 100;
        _slider.StepFrequency = 1;
        _slider.VerticalAlignment = VerticalAlignment.Center;
        _slider.HorizontalAlignment = HorizontalAlignment.Stretch;
        _slider.IsTabStop = false;
        _slider.ValueChanged += OnSliderChanged;

        // Handled events too: Slider marks its own pointer events handled, so a plain subscription
        // never sees the press. Four events rather than one, for the same reason a row's hover
        // needs four — the pointer leaves without an Exited when the capture is taken or the
        // widget is torn down mid-drag, and either would strand the tile refusing to update.
        _grab = OnGrab;
        _release = OnRelease;
        _slider.AddHandler(PointerPressedEvent, _grab, true);
        _slider.AddHandler(PointerReleasedEvent, _release, true);
        _slider.AddHandler(PointerCaptureLostEvent, _release, true);
        _slider.AddHandler(PointerCanceledEvent, _release, true);

        _band.ColumnSpacing = Fluent.Space2;
        _band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _band.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _band.Margin = WidgetLayout.FooterInset;

        SetColumn(_slider, 1);
        _band.Children.Add(_mute);
        _band.Children.Add(_slider);
        SetRow(_band, 4);

        var definition = WidgetCatalog.Definition(WidgetKind.Volume);
        _empty = Fluent.EmptyState(definition.Glyph, _emptyText, out _);
        _empty.Visibility = Visibility.Collapsed;
        SetRowSpan(_empty, 5);

        Children.Add(_reading);
        Children.Add(_apps);
        Children.Add(_micBlock);
        Children.Add(_meterHost);
        Children.Add(_band);
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
        // [S-1220] No ticker: the level moves when Core Audio says it moved.
        _detached = false;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as VolumeConfig ?? new VolumeConfig();
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
        : [new WidgetAction(Glyphs.Speakers, "Follow the default output", () => PickDevice(null))];

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        Lay();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        // [S-1223] Off screen: stop the meter.
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
        ShowPeaks();
    }

    /// <summary>The level on screen: a dragged one Windows has not confirmed yet, else the device's.</summary>
    private (double Level, bool Muted)? Current()
    {
        if (_snapshot?.Output is not { } output)
        {
            return null;
        }

        return _overrides.TryGetValue(MixerRules.MasterKey, out var held)
            ? (held, output.Muted && held <= 0)
            : (output.Level, output.Muted);
    }

    /// <summary>[S-1230] The ladder. Runs on a new area, a new config and a new snapshot.</summary>
    private void Lay()
    {
        if (_detached)
        {
            return;
        }

        var measured = _area.IsMeasured;
        // A short tile keeps a smaller foot under the slider: on a 120 px square, 8 px of it is the
        // difference between having a slider and not.
        var foot = _area.HeightClass == SizeClass.Compact ? Fluent.Space2 : WidgetLayout.ContentInset;
        var box = _area.Deflate(WidgetLayout.ContentInset * 2, foot);
        _band.Margin = new Thickness(WidgetLayout.ContentInset, 0, WidgetLayout.ContentInset, foot);
        var room = new Disclosure(box, gap: _area.HeightClass == SizeClass.Compact ? Fluent.Space1 : Fluent.Space2);

        // Rung 0: the number, at its smallest.
        room.Reserve(FluentType.BodyLarge.LineHeight);

        // Rung 1: the slider band.
        var band = _config.ShowSlider && room.Take(Fluent.RowHeightCompact);

        // Rung 2: [S-1223] the meter, from a medium tile (or along a wide strip).
        var peaks = _config.ShowPeaks && _loaded
            && (_area.Least >= SizeClass.Regular || _area.WidthClass >= SizeClass.Expanded)
            && room.Take(LevelRow.MeterHeight);

        // Rung 3: [S-1224] the microphone, on a large tile.
        var large = (_area.WidthClass >= SizeClass.Expanded && _area.HeightClass >= SizeClass.Regular)
            || (_area.WidthClass >= SizeClass.Regular && _area.HeightClass >= SizeClass.Expanded);
        var mic = _config.ShowMicrophone && large && room.Take(FluentType.Caption.LineHeight + Fluent.RowHeight);

        // Rung 4: [S-1231] the apps playing on the device, on a large tile with room left.
        var appRows = _config.ShowApps && large
            && room.Take(FluentType.Caption.LineHeight)
            ? room.TakeRows(AppLineHeight, _area.HeightClass >= SizeClass.Huge ? TallAppCount : TopAppCount)
            : 0;

        // [S-1222] The device switcher rides on the caption, on large tiles.
        _switcher = large && _surface is { IsPreview: false };
        _peaksOn = peaks;

        _feed.Want(new AudioWant(_config.DeviceId, Sessions: appRows > 0, Peaks: peaks, Microphone: mic));

        if (_snapshot is not { } snapshot || !measured)
        {
            return;
        }

        if (snapshot.Status != AudioStatus.Ready || Current() is not { } level)
        {
            _emptyText.Text = snapshot.Status switch
            {
                AudioStatus.DeviceMissing => $"{AudioRules.DeviceName(_config.DeviceId, snapshot.Devices, _config.DeviceName)} is not connected",
                AudioStatus.NoDevice => "No output device",
                _ => "Sound is not available",
            };

            _empty.Visibility = Visibility.Visible;
            _reading.Visibility = _band.Visibility = _apps.Visibility = _micBlock.Visibility = _meterHost.Visibility = Visibility.Collapsed;
            return;
        }

        // [S-446] Keep a dragged level until a snapshot confirms it.
        var master = new MixerRow(MixerRules.MasterKey, string.Empty, null, snapshot.Output!.Level, snapshot.Output.Muted, true, false, 0);
        IReadOnlyList<MixerRow> confirmed = snapshot.Microphone is { } input
            ? [master, new MixerRow(MixerRules.MicrophoneKey, input.Name, null, input.Level, input.Muted, true, false, 0)]
            : [master];
        _overrides = MixerRules.ReconcileOverrides(_overrides, confirmed);
        level = Current() ?? level;

        _empty.Visibility = Visibility.Collapsed;
        _reading.Visibility = Visibility.Visible;
        _band.Visibility = band ? Visibility.Visible : Visibility.Collapsed;
        _meterHost.Visibility = peaks ? Visibility.Visible : Visibility.Collapsed;
        _micBlock.Visibility = mic && snapshot.Microphone is not null ? Visibility.Visible : Visibility.Collapsed;
        _apps.Visibility = appRows > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Read-only is a state of the controls, not a reason to hide them: a greyed slider still
        // shows the level, and a tile that drops its slider when control is switched off looks
        // like a different widget.
        _slider.IsEnabled = _config.AllowControl;
        _mute.IsEnabled = _config.AllowControl;
        _mic.Root.IsHitTestVisible = _config.AllowControl;

        ShowLevel(level.Level, level.Muted, snapshot);

        // The meter sits between the slider band and whatever is above it, inset like the slider.
        _meterHost.Margin = new Thickness(WidgetLayout.ContentInset + Fluent.RowHeightCompact + Fluent.Space2, 0, WidgetLayout.ContentInset, band ? 0 : foot);

        if (mic && snapshot.Microphone is { } microphone)
        {
            _micName.Text = microphone.Name.Length > 0 ? microphone.Name : "Microphone";
            ToolTipService.SetToolTip(_micName, _micName.Text);
            var held = _overrides.TryGetValue(MixerRules.MicrophoneKey, out var micLevel) ? micLevel : microphone.Level;
            _mic.Show(held, microphone.Muted, microphone.Muted ? "Unmute the microphone" : "Mute the microphone");
        }

        ShowApps(appRows, snapshot);
        ShowPeaks();

        // Whatever the rungs left goes to the number. Four characters: "100%" is the widest this
        // readout can ever be — fitting to the live text would make "9%" and "100%" pick different
        // steps, and the number would change size between two changes a moment apart.
        var captionBeside = box.Width >= CaptionMinimumWidth;
        var heroWidth = captionBeside ? box.Width / 2 : box.Width;
        var hero = FluentType.Hero(heroWidth, Math.Min(room.Remaining + FluentType.BodyLarge.LineHeight, FluentType.Display.LineHeight), 4);

        if (hero.FontSize < FluentType.BodyLarge.FontSize)
        {
            hero = FluentType.BodyLarge;
        }

        if (hero != _heroStep)
        {
            _heroStep = hero;
            _readout.Apply(hero);
        }

        var caption = hero.FontSize >= FluentType.Title.FontSize ? FluentType.Body : FluentType.Caption;
        if (caption != _captionStep)
        {
            _captionStep = caption;
            _caption.Apply(caption);
        }

        _device.Visibility = captionBeside ? Visibility.Visible : Visibility.Collapsed;
        _device.IsHitTestVisible = _switcher;
        _device.IsTabStop = false;
        _chevron.Visibility = _switcher ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(_device, _switcher ? "Choose the output device" : null);
    }

    private void ShowLevel(double level, bool muted, AudioSnapshot snapshot)
    {
        _readout.Text = muted ? "0%" : VolumeRules.Readout(level);

        // [S-1232] The device's name where "Output level" used to be.
        _caption.Text = VolumeRules.Caption(level, muted, snapshot.Output?.Name);

        // Muted is a colour, not an opacity: the disabled text brush is what every other disabled
        // thing on the desktop uses and it follows the theme.
        _readout.Foreground = muted ? _mutedBrush : _readingBrush;
        _muteIcon.Glyph = LevelRow.OutputGlyph(level, muted);
        ToolTipService.SetToolTip(_mute, muted ? "Unmute" : "Mute");

        // Echoing: this is a snapshot writing back what it just read, and the ValueChanged it raises
        // must not be mistaken for somebody moving the control.
        if (!_grabbed)
        {
            _echoing = true;
            _slider.Value = muted ? 0 : VolumeRules.Percent(level);
            _echoing = false;
        }
    }

    /// <summary>[S-1231] The top apps as read-only level bars, playing ones first.</summary>
    private void ShowApps(int count, AudioSnapshot snapshot)
    {
        var rows = count > 0
            ? MixerRules.TopApps(MixerRules.Group(snapshot.Sessions, grouped: true), count)
            : [];

        while (_appLines.Count < rows.Count)
        {
            var line = new AppLine();
            _appLines.Add(line);
            _apps.Children.Add(line.Root);
        }

        for (var i = 0; i < _appLines.Count; i++)
        {
            if (i < rows.Count)
            {
                _appLines[i].Show(rows[i]);
            }
            else
            {
                _appLines[i].Root.Visibility = Visibility.Collapsed;
            }
        }

        _appsCaption.Text = rows.Count == 0 ? "No app is playing audio" : "Apps on this device";
    }

    private void ShowPeaks()
    {
        var on = _peaksOn && Current() is { Muted: false };
        _meterScale.ScaleX = on ? AudioRules.Peak(_peaks.Output) : 0;
        _mic.ShowPeak(_peaks.Microphone, _peaksOn);
    }

    // ---- writes -------------------------------------------------------------------------------

    private void SetLevel(double level)
    {
        if (_detached || !_config.AllowControl || Current() is not { } current)
        {
            return;
        }

        _overrides[MixerRules.MasterKey] = level;

        // Moving a muted endpoint above silence unmutes it, which is what the volume flyout does
        // and what anybody dragging a slider expects.
        if (current.Muted && level > 0)
        {
            _feed.SetMute(MixerRules.MasterKey, false);
        }

        _feed.SetLevel(MixerRules.MasterKey, level);
    }

    private void ToggleMute()
    {
        if (_detached || !_config.AllowControl || Current() is not { } current)
        {
            return;
        }

        _feed.SetMute(MixerRules.MasterKey, !current.Muted);
    }

    private void OnMicLevel(double level)
    {
        if (_detached || !_config.AllowControl || _snapshot?.Microphone is not { } mic)
        {
            return;
        }

        _overrides[MixerRules.MicrophoneKey] = level;

        if (mic.Muted && level > 0)
        {
            _feed.SetMute(MixerRules.MicrophoneKey, false);
        }

        _feed.SetLevel(MixerRules.MicrophoneKey, level);
    }

    private void OnMicMute()
    {
        if (!_detached && _config.AllowControl && _snapshot?.Microphone is { } mic)
        {
            _feed.SetMute(MixerRules.MicrophoneKey, !mic.Muted);
        }
    }

    private void OnGrab(object sender, PointerRoutedEventArgs args) => _grabbed = true;

    private void OnRelease(object sender, PointerRoutedEventArgs args)
    {
        _grabbed = false;

        // Show what the machine holds now; a held level stays until a snapshot confirms it.
        Lay();
    }

    private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_echoing || Math.Abs(args.NewValue - args.OldValue) < 0.5)
        {
            return;
        }

        SetLevel(VolumeRules.Scalar(args.NewValue));
    }

    private void OnMuteClicked(object sender, RoutedEventArgs args) => ToggleMute();

    // ---- keys ([S-1226]) ------------------------------------------------------------------------

    public bool HandleKey(WidgetKey key)
    {
        if (_detached || !_config.AllowControl || Current() is not { } current)
        {
            return false;
        }

        var action = AudioRules.VolumeKey(key);

        switch (action.Kind)
        {
            case AudioKeyKind.Step:
                SetLevel(AudioRules.Step(current.Muted ? 0 : current.Level, action.Delta));
                Lay();
                return true;

            case AudioKeyKind.ToggleMute:
                ToggleMute();
                return true;

            default:
                return false;
        }
    }

    // ---- the device switcher ([S-1222]) -------------------------------------------------------

    private void OnDeviceClicked(object sender, RoutedEventArgs args)
    {
        if (_switcher && _snapshot is { } snapshot)
        {
            AudioDeviceMenu.Build(snapshot, _config.DeviceId, PickDevice).ShowAt(_device);
        }
    }

    private void PickDevice(AudioDevice? device)
    {
        if (_surface is not { IsPreview: false } surface)
        {
            return;
        }

        _config.DeviceId = device?.Id;
        _config.DeviceName = device?.Name;
        _overrides.Clear();
        surface.RequestSave();
        Lay();
    }

    public void Detach()
    {
        _detached = true;

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _slider.ValueChanged -= OnSliderChanged;
        _slider.RemoveHandler(PointerPressedEvent, _grab);
        _slider.RemoveHandler(PointerReleasedEvent, _release);
        _slider.RemoveHandler(PointerCaptureLostEvent, _release);
        _slider.RemoveHandler(PointerCanceledEvent, _release);
        _mute.Click -= OnMuteClicked;
        _device.Click -= OnDeviceClicked;
        _mic.Detach();
        _feed.Dispose();
    }

    /// <summary>[S-1231] One app's read-only line: name, its level as a bar, percent.</summary>
    private sealed class AppLine
    {
        /// <summary>A playing app's bar is the accent; a silent one's is the quiet control fill.</summary>
        private static readonly Brush PlayingBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

        private static readonly Brush QuietBrush = FluentLayout.Themed("ControlStrongFillColorDefaultBrush", Colors.Gray);

        private readonly TextBlock _name = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        private readonly TextBlock _percent = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly Rectangle _track = new() { Height = 4, RadiusX = 2, RadiusY = 2 };
        private readonly Rectangle _level = new() { Height = 4, RadiusX = 2, RadiusY = 2 };
        private readonly ScaleTransform _levelScale = new() { ScaleX = 0 };

        public AppLine()
        {
            Root.Height = AppLineHeight;
            Root.ColumnSpacing = Fluent.Space2;
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Fluent.Space6 + Fluent.Space3) });

            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _name.VerticalAlignment = VerticalAlignment.Center;
            _percent.TextAlignment = TextAlignment.Right;
            _percent.VerticalAlignment = VerticalAlignment.Center;

            _track.Fill = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);
            _level.Fill = PlayingBrush;
            _level.RenderTransform = _levelScale;
            _level.RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5);

            SetColumn(_track, 1);
            SetColumn(_level, 1);
            SetColumn(_percent, 2);
            Root.Children.Add(_name);
            Root.Children.Add(_track);
            Root.Children.Add(_level);
            Root.Children.Add(_percent);
        }

        public Grid Root { get; } = new();

        public void Show(MixerRow row)
        {
            Root.Visibility = Visibility.Visible;
            _name.Text = row.Name.Length > 0 ? row.Name : "Unknown app";
            ToolTipService.SetToolTip(Root, row.Active ? _name.Text : $"{_name.Text} (not playing)");
            _percent.Text = $"{MixerRules.DisplayPercent(row)}%";
            _levelScale.ScaleX = row.Muted ? 0 : MixerRules.ClampLevel(row.Level);
            _level.Fill = row.Active ? PlayingBrush : QuietBrush;
        }
    }
}
