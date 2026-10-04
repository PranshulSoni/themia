using Glyphs = Themia.App.Shell.Glyphs;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-1224] One level control: a mute button, a slider, the percent and — when asked — a live meter
/// under the slider. The mixer's side panel uses it for the device and the microphone, the volume
/// tile for its microphone row.
/// <para>
/// The slider cannot fight the hand on it: a value written from a snapshot is an echo, not a
/// change, and while the thumb is held snapshots leave it alone (the same two guards the mixer
/// rows and the volume tile's own slider have).
/// </para>
/// </summary>
internal sealed class LevelRow
{
    /// <summary>The meter's thickness: a hairline is lost on a busy wallpaper, more competes with the track.</summary>
    public const double MeterHeight = 2;

    private readonly Button _mute = new();
    private readonly FontIcon _muteIcon;
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, StepFrequency = 1 };
    private readonly TextBlock _percent = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
    private readonly Rectangle _meter = new() { Height = MeterHeight, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ScaleTransform _meterScale = new() { ScaleX = 0 };
    private readonly Func<double, bool, string> _glyph;
    private readonly Action<double> _onLevel;
    private readonly Action _onMute;
    private readonly PointerEventHandler _grab;
    private readonly PointerEventHandler _release;
    private bool _echoing;
    private bool _grabbed;
    private bool _muted;

    /// <param name="glyph">The mute button's glyph for a level and mute state.</param>
    /// <param name="onLevel">The user moved the slider (0..1).</param>
    /// <param name="onMute">The user pressed the mute button.</param>
    public LevelRow(Func<double, bool, string> glyph, Action<double> onLevel, Action onMute)
    {
        _glyph = glyph;
        _onLevel = onLevel;
        _onMute = onMute;

        _muteIcon = Fluent.Icon(glyph(1, false), "TextFillColorPrimaryBrush");
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
        _slider.Margin = new Thickness(Fluent.Space2, 0, Fluent.Space2, 0);
        _slider.ValueChanged += OnSliderChanged;

        _grab = (_, _) => _grabbed = true;
        _release = (_, _) => _grabbed = false;
        _slider.AddHandler(UIElement.PointerPressedEvent, _grab, true);
        _slider.AddHandler(UIElement.PointerReleasedEvent, _release, true);
        _slider.AddHandler(UIElement.PointerCaptureLostEvent, _release, true);
        _slider.AddHandler(UIElement.PointerCanceledEvent, _release, true);

        _percent.TextAlignment = TextAlignment.Right;
        _percent.MinWidth = Fluent.Space6 + Fluent.Space3;
        _percent.VerticalAlignment = VerticalAlignment.Center;
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);

        _meter.Fill = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        _meter.RenderTransform = _meterScale;
        _meter.RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5);
        _meter.VerticalAlignment = VerticalAlignment.Bottom;

        // Under the track, inset like the track is so the meter's ends line up with the thumb's travel.
        _meter.Margin = new Thickness(Fluent.Space2 + Fluent.Space2, 0, Fluent.Space2 + Fluent.Space2, Fluent.Space1);
        _meter.Visibility = Visibility.Collapsed;

        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.Height = Fluent.RowHeight;

        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_meter, 1);
        Grid.SetColumn(_percent, 2);
        Root.Children.Add(_mute);
        Root.Children.Add(_slider);
        Root.Children.Add(_meter);
        Root.Children.Add(_percent);
    }

    public Grid Root { get; } = new();

    /// <summary>Show a level read from the machine. Leaves a held thumb alone.</summary>
    public void Show(double level, bool muted, string muteTip)
    {
        _muted = muted;
        var percent = muted ? 0 : VolumeRules.Percent(level);
        _percent.Text = LocaleFormat.Percent(Strings.Current, percent);
        _muteIcon.Glyph = _glyph(level, muted);
        ToolTipService.SetToolTip(_mute, muteTip);

        if (!_grabbed)
        {
            _echoing = true;
            _slider.Value = percent;
            _echoing = false;
        }
    }

    /// <summary>[S-1223] The meter, or none.</summary>
    public void ShowPeak(double peak, bool visible)
    {
        _meter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _meterScale.ScaleX = visible && !_muted ? AudioRules.Peak(peak) : 0;
    }

    private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_echoing || Math.Abs(args.NewValue - args.OldValue) < 0.5)
        {
            return;
        }

        _onLevel(VolumeRules.Scalar(args.NewValue));
    }

    private void OnMuteClicked(object sender, RoutedEventArgs args) => _onMute();

    public void Detach()
    {
        _mute.Click -= OnMuteClicked;
        _slider.ValueChanged -= OnSliderChanged;
        _slider.RemoveHandler(UIElement.PointerPressedEvent, _grab);
        _slider.RemoveHandler(UIElement.PointerReleasedEvent, _release);
        _slider.RemoveHandler(UIElement.PointerCaptureLostEvent, _release);
        _slider.RemoveHandler(UIElement.PointerCanceledEvent, _release);
    }

    /// <summary>The speaker glyph for an output level: Windows' four, or the crossed-out one when muted.</summary>
    public static string OutputGlyph(double level, bool muted) => VolumeRules.Level(level, muted) switch
    {
        VolumeLevel.Muted => Glyphs.Mute,
        VolumeLevel.Silent => Glyphs.Volume0,
        VolumeLevel.Low => Glyphs.Volume1,
        VolumeLevel.Medium => Glyphs.Volume2,
        _ => Glyphs.Volume3,
    };

    /// <summary>[S-1224] The microphone glyph, crossed out when muted.</summary>
    public static string MicrophoneGlyph(double level, bool muted) => muted ? Glyphs.MicrophoneOff : Glyphs.Microphone;
}
