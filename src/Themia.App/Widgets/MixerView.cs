using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>
/// [S-440] Per-application volume, as the Windows volume mixer lays it out: the device's master level
/// on top, then one row per app with its icon, its name, a slider and a mute button.
/// <para>
/// The rows are reused by position, never rebuilt. Each snapshot produces fresh row data, and
/// replacing the row visuals would destroy the slider under the user's pointer mid-drag — the
/// original hit exactly that and had to switch its list to position keying for the same reason.
/// </para>
/// </summary>
internal sealed partial class MixerView : Grid, IWidgetView
{
    /// <summary>From this width a row is one line: name, slider, percent and mute side by side.</summary>
    private const double OneLineWidth = 360;

    /// <summary>Below this width there is no room for a slider: a row is the app, its level and its mute.</summary>
    private const double SliderWidth = 170;

    private static readonly string[] LevelGlyphs = [Glyphs.Mute, Glyphs.Volume0, Glyphs.Volume1, Glyphs.Volume2, Glyphs.Volume3];

    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText = new();
    private readonly List<RowVisual> _rows = [];

    private MixerConfig _config = new();
    private IDisposable? _subscription;
    private MixerSnapshot? _snapshot;
    private Dictionary<string, double> _overrides = new(StringComparer.Ordinal);
    private Layout _layout = Layout.TwoLine;
    private bool _detached;

    public MixerView()
    {
        _scroller = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = Fluent.ListPadding,
        };

        _empty = Fluent.EmptyState(Glyphs.Mixer, _emptyText, out _);
        _empty.Visibility = Visibility.Collapsed;

        Children.Add(_scroller);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    private enum Layout
    {
        /// <summary>No slider: icon, name, percent, mute.</summary>
        Compact,

        /// <summary>Name, percent and mute on top, the slider under them.</summary>
        TwoLine,

        /// <summary>Everything on one line.</summary>
        OneLine,
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        _config = config as MixerConfig ?? new MixerConfig();

        // [S-440] No ticker: the list moves when Core Audio says something moved.
        _subscription ??= AudioSessions.Subscribe(OnSnapshot);
        Render();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as MixerConfig ?? new MixerConfig();
        Render();
    }

    private void OnSnapshot(MixerSnapshot snapshot)
    {
        if (_detached)
        {
            return;
        }

        _snapshot = snapshot;
        Render();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var layout = args.NewSize.Width >= OneLineWidth ? Layout.OneLine
            : args.NewSize.Width >= SliderWidth ? Layout.TwoLine
            : Layout.Compact;

        if (layout == _layout)
        {
            return;
        }

        _layout = layout;
        foreach (var row in _rows)
        {
            row.Arrange(layout);
        }
    }

    private void Render()
    {
        if (_detached || _snapshot is not { } snapshot)
        {
            return;
        }

        if (!snapshot.Available)
        {
            // [S-448] An intentional empty state, not a blank card.
            ShowEmpty("No output device");
            return;
        }

        var grouped = MixerRules.Group(snapshot.Sessions, _config.GroupByApp);
        var rows = MixerRules.BuildRows(
            grouped,
            snapshot.Master,
            new MixerRowOptions(_config.ShowMaster, _config.ShowSystemSounds, _config.OnlyActive));

        // [S-446] Keep each dragged level until a snapshot confirms it or its app is gone.
        _overrides = MixerRules.ReconcileOverrides(_overrides, rows);
        rows = MixerRules.ApplyOverrides(rows, _overrides);

        if (rows.Count == 0)
        {
            ShowEmpty("No app is playing audio");
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroller.Visibility = Visibility.Visible;

        while (_rows.Count < rows.Count)
        {
            var visual = new RowVisual(this);
            visual.Arrange(_layout);
            _rows.Add(visual);
            _list.Children.Add(visual.Host);
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            if (i < rows.Count)
            {
                _rows[i].Host.Visibility = Visibility.Visible;
                _rows[i].Show(rows[i], _config.ShowIcons);
            }
            else
            {
                _rows[i].Host.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowEmpty(string message)
    {
        _emptyText.Text = message;
        _empty.Visibility = Visibility.Visible;
        _scroller.Visibility = Visibility.Collapsed;
    }

    /// <summary>[S-446] The user moved a slider: hold the value on screen and queue the write.</summary>
    private void OnLevel(MixerRow row, double level)
    {
        _overrides[row.Key] = level;

        // [S-447] A muted row dragged above zero unmutes, as the volume widget and the flyout do —
        // a slider that moves but stays silent reads as broken.
        if (row.Muted && level > 0)
        {
            AudioSessions.SetMute(row.Key, false);
        }

        AudioSessions.SetLevel(row.Key, level);
    }

    private static void OnMute(MixerRow row) => AudioSessions.SetMute(row.Key, !row.Muted);

    private void OnReset(MixerRow row)
    {
        _overrides[row.Key] = 1;

        if (row.Muted)
        {
            AudioSessions.SetMute(row.Key, false);
        }

        AudioSessions.SetLevel(row.Key, 1);
    }

    public void Detach()
    {
        _detached = true;
        SizeChanged -= OnSizeChanged;
        _subscription?.Dispose();
        _subscription = null;

        foreach (var row in _rows)
        {
            row.Detach();
        }

        _rows.Clear();
        _list.Children.Clear();
    }

    private static string RowName(MixerRow row) => row.IsMaster ? "Master"
        : row.IsSystem ? "System sounds"
        : row.Name.Length > 0 ? row.Name
        : "Unknown app";

    /// <summary>One row's visuals, rewritten in place for whichever row sits at its position.</summary>
    private sealed class RowVisual
    {
        private static readonly Brush PrimaryBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        private static readonly Brush MutedBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);

        private readonly MixerView _owner;
        private readonly Grid _grid = new();
        private readonly Grid _iconSlot = new() { Width = Fluent.Space5, Height = Fluent.Space5 };
        private readonly Image _image = new() { Width = Fluent.Space5, Height = Fluent.Space5, Stretch = Stretch.Uniform };
        private readonly FontIcon _glyph = Fluent.Icon(Glyphs.Apps, "TextFillColorSecondaryBrush");
        private readonly TextBlock _name = Fluent.Text(FluentType.Body);
        private readonly Border _count;
        private readonly TextBlock _countText = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        private readonly TextBlock _percent = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, StepFrequency = 1 };
        private readonly Button _mute = new();
        private readonly FontIcon _muteIcon = Fluent.Icon(Glyphs.Volume3, "TextFillColorPrimaryBrush");
        private readonly IDisposable _hover;
        private readonly PointerEventHandler _grab;
        private readonly PointerEventHandler _release;
        private readonly HugRow _nameLine;

        private MixerRow? _row;
        private string? _iconPath;
        private Layout _layout = Layout.TwoLine;
        private bool _icons = true;
        private bool _echoing;
        private bool _grabbed;

        public RowVisual(MixerView owner)
        {
            _owner = owner;

            _iconSlot.Children.Add(_glyph);
            _iconSlot.Children.Add(_image);
            _iconSlot.VerticalAlignment = VerticalAlignment.Center;

            _name.VerticalAlignment = VerticalAlignment.Center;

            // [S-442] How many sessions the one slider drives — a quiet pill, not an unread badge.
            _countText.VerticalAlignment = VerticalAlignment.Center;
            _count = new Border
            {
                Child = _countText,
                Background = FluentLayout.Themed("ControlFillColorSecondaryBrush", Colors.Gray),
                CornerRadius = new CornerRadius(Fluent.OverlayRadius),
                Padding = new Thickness(Fluent.Space1 + 2, 0, Fluent.Space1 + 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };

            // The count hugs the name, and the name ellipsises rather than running under the slider.
            _nameLine = new HugRow(_name, _count, Fluent.Space2) { VerticalAlignment = VerticalAlignment.Center };

            _percent.TextAlignment = TextAlignment.Right;
            _percent.MinWidth = Fluent.Space6 + Fluent.Space2;
            _percent.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);

            _mute.Content = _muteIcon;
            _mute.Padding = new Thickness(Fluent.Space1 + 2);
            _mute.MinWidth = 0;
            _mute.MinHeight = 0;
            _mute.Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
            _mute.BorderThickness = new Thickness(0);
            _mute.CornerRadius = new CornerRadius(Fluent.ControlRadius);
            _mute.VerticalAlignment = VerticalAlignment.Center;
            _mute.Click += OnMuteClicked;

            _slider.VerticalAlignment = VerticalAlignment.Center;
            _slider.ValueChanged += OnSliderChanged;

            // Handled events too: the slider marks its own pointer events handled. Four of them, for
            // the same reason a row's hover needs four — the pointer can leave without a release.
            _grab = (_, _) => _grabbed = true;
            _release = (_, _) => _grabbed = false;
            _slider.AddHandler(PointerPressedEvent, _grab, true);
            _slider.AddHandler(PointerReleasedEvent, _release, true);
            _slider.AddHandler(PointerCaptureLostEvent, _release, true);
            _slider.AddHandler(PointerCanceledEvent, _release, true);

            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _grid.Children.Add(_iconSlot);
            _grid.Children.Add(_nameLine);
            _grid.Children.Add(_slider);
            _grid.Children.Add(_percent);
            _grid.Children.Add(_mute);

            Host = Fluent.Row(_grid);
            Host.RightTapped += OnRightTapped;
            _hover = Fluent.Hover(Host);
        }

        public Border Host { get; }

        /// <summary>
        /// [S-449] Put the parts where this width wants them. Gaps are margins on the parts rather
        /// than grid spacing, because spacing is paid between empty columns too, and a narrow row
        /// cannot afford thirty pixels of gaps around nothing.
        /// </summary>
        public void Arrange(Layout layout)
        {
            _layout = layout;
            Place(_iconSlot, 0, 0, 1);

            switch (layout)
            {
                case Layout.OneLine:
                    Place(_nameLine, 0, 1, 1);
                    Place(_slider, 0, 2, 1);
                    Place(_percent, 0, 3, 1);
                    Place(_mute, 0, 4, 1);
                    _slider.Margin = new Thickness(Fluent.Space3, 0, 0, 0);
                    break;

                case Layout.TwoLine:
                    Place(_nameLine, 0, 1, 2);
                    Place(_percent, 0, 3, 1);
                    Place(_mute, 0, 4, 1);
                    Place(_slider, 1, 1, 4);

                    // Tucked up under the name line: the slider's own box is taller than its track.
                    _slider.Margin = new Thickness(_icons ? Fluent.Space2 : 0, -Fluent.Space2, Fluent.Space2, -Fluent.Space1);
                    break;

                default:
                    Place(_nameLine, 0, 1, 2);
                    Place(_percent, 0, 3, 1);
                    Place(_mute, 0, 4, 1);
                    break;
            }

            _slider.Visibility = layout == Layout.Compact ? Visibility.Collapsed : Visibility.Visible;

            // A compact row is the app, its level and its mute: the icon names the app, and the
            // name only comes back when there is no icon to do it.
            _nameLine.Visibility = layout == Layout.Compact && _icons ? Visibility.Collapsed : Visibility.Visible;
            _nameLine.Margin = new Thickness(_icons ? Fluent.Space2 : 0, 0, 0, 0);
            _percent.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
            _mute.Margin = new Thickness(Fluent.Space1, 0, 0, 0);

            Host.Padding = layout == Layout.Compact
                ? new Thickness(Fluent.Space3, 0, Fluent.Space1, 0)
                : new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space2, Fluent.Space1);
        }

        private static void Place(FrameworkElement element, int row, int column, int span)
        {
            SetRow(element, row);
            SetColumn(element, column);
            SetColumnSpan(element, span);
        }

        public void Show(MixerRow row, bool showIcons)
        {
            _row = row;

            var name = RowName(row);
            _name.Text = name;
            _name.Foreground = row.Muted ? MutedBrush : PrimaryBrush;
            ToolTipService.SetToolTip(Host, name);

            _count.Visibility = row.Sessions > 1 ? Visibility.Visible : Visibility.Collapsed;
            _countText.Text = row.Sessions.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var percent = MixerRules.DisplayPercent(row);
            _percent.Text = $"{percent}%";

            var level = VolumeRules.Level(row.Level, row.Muted);
            _muteIcon.Glyph = LevelGlyphs[(int)level];
            ToolTipService.SetToolTip(_mute, row.Muted ? "Unmute" : "Mute");

            // A snapshot is the machine's truth, but not under a held thumb: writing it there would
            // snap the slider back to where the app was a moment ago.
            if (!_grabbed)
            {
                _echoing = true;
                _slider.Value = percent;
                _echoing = false;
            }

            ShowIcon(row, showIcons);
        }

        /// <summary>[S-441] The app's own icon when there is one, else a glyph for what the row is.</summary>
        private void ShowIcon(MixerRow row, bool showIcons)
        {
            _iconSlot.Visibility = showIcons ? Visibility.Visible : Visibility.Collapsed;

            if (showIcons != _icons)
            {
                _icons = showIcons;
                Arrange(_layout);
            }

            if (!showIcons)
            {
                return;
            }

            _glyph.Glyph = row.IsMaster ? Glyphs.Volume : row.IsSystem ? Glyphs.Bell : Glyphs.Apps;

            if (row.Path is not { } path)
            {
                _iconPath = null;
                _image.Source = null;
                _glyph.Visibility = Visibility.Visible;
                return;
            }

            if (string.Equals(_iconPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _iconPath = path;
            _image.Source = null;
            _glyph.Visibility = Visibility.Visible;
            _ = LoadIconAsync(path);
        }

        private async Task LoadIconAsync(string path)
        {
            var image = await AppIcons.GetAsync(path).ConfigureAwait(true);

            // The row may show another app by now.
            if (!string.Equals(_iconPath, path, StringComparison.OrdinalIgnoreCase) || image is null)
            {
                return;
            }

            _image.Source = image;
            _glyph.Visibility = Visibility.Collapsed;
        }

        private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            if (_echoing || _row is not { } row || Math.Abs(args.NewValue - args.OldValue) < 0.5)
            {
                return;
            }

            _owner.OnLevel(row, MixerRules.ClampLevel(args.NewValue / 100));
        }

        private void OnMuteClicked(object sender, RoutedEventArgs args)
        {
            if (_row is { } row)
            {
                OnMute(row);
            }
        }

        /// <summary>[S-447] Right-clicking a row: mute or unmute it, or put it back to 100%.</summary>
        private void OnRightTapped(object sender, RightTappedRoutedEventArgs args)
        {
            if (_row is not { } row)
            {
                return;
            }

            var menu = new MenuFlyout();

            var mute = new MenuFlyoutItem
            {
                Text = row.Muted ? "Unmute" : "Mute",
                Icon = new FontIcon { Glyph = row.Muted ? Glyphs.Volume3 : Glyphs.Mute },
            };
            mute.Click += (_, _) => OnMute(row);

            var reset = new MenuFlyoutItem { Text = "Reset to 100%", Icon = new FontIcon { Glyph = Glyphs.Reset } };
            reset.Click += (_, _) => _owner.OnReset(row);

            menu.Items.Add(mute);
            menu.Items.Add(reset);
            menu.ShowAt(Host, args.GetPosition(Host));
            args.Handled = true;
        }

        public void Detach()
        {
            _hover.Dispose();
            Host.RightTapped -= OnRightTapped;
            _mute.Click -= OnMuteClicked;
            _slider.ValueChanged -= OnSliderChanged;
            _slider.RemoveHandler(PointerPressedEvent, _grab);
            _slider.RemoveHandler(PointerReleasedEvent, _release);
            _slider.RemoveHandler(PointerCaptureLostEvent, _release);
            _slider.RemoveHandler(PointerCanceledEvent, _release);
        }
    }
}
