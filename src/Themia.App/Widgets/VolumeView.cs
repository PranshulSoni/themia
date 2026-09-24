using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The master output level, as the volume flyout would show it.
/// <para>
/// A readout first: the level is read from the default render endpoint every tick, so the tile
/// follows the volume keys, the flyout, and whatever a game just did to the mixer. The slider is
/// the second job, and it is built so it cannot fight the first — while the thumb is held, the poll
/// stops writing to it, and everything the tile sets goes through the same endpoint the rest of
/// Windows sets.
/// </para>
/// <para>
/// <b>Polled, not called back.</b> Core Audio will call an <c>IAudioEndpointVolumeCallback</c> when
/// the level moves, but registering one means handing it a COM object of ours — a callback the
/// runtime has to be able to build a native wrapper for, called on an arbitrary thread, which must
/// then be unregistered from that same thread's teardown. A read is four local COM calls, so asking
/// twice a second costs less than being called correctly.
/// </para>
/// </summary>
internal sealed partial class VolumeView : Grid, IWidgetView
{
    /// <summary>Below this the caption beside the number is dropped: the number is the point.</summary>
    private const double CaptionMinimumWidth = 200;

    // Segoe Fluent Icons code points this view needs and the shared table does not carry. Written
    // as code points for the same reason that table does: a private-use character in a source file
    // cannot be read, diffed or reviewed.
    private static readonly string[] LevelGlyphs =
    [
        From(0xE74F), // muted
        From(0xE992), // silent
        From(0xE993), // low
        From(0xE994), // medium
        From(0xE995), // high
    ];

    private readonly TextBlock _readout = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly Grid _reading = new();
    private readonly Slider _slider = new();
    private readonly Button _mute = new();
    private readonly FontIcon _muteIcon;
    private readonly Grid _band = new();
    private readonly StackPanel _empty;

    // Looked up once: the readout swaps between them on every sample, and a resource lookup per
    // sample is a dictionary hit and a possible log line for nothing.
    private readonly Brush _readingBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
    private readonly Brush _mutedBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);

    private Ticker? _ticker;
    private VolumeConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    private VolumeReading _current = VolumeReading.Unavailable;
    private TypeStep _heroStep = FluentType.Subtitle;
    private TypeStep _captionStep = FluentType.Caption;

    /// <summary>
    /// True while the slider is being written from a sample rather than by a person. The slider
    /// raises <c>ValueChanged</c> either way, and without this every poll would write the level it
    /// had just read straight back into the mixer.
    /// </summary>
    private bool _echoing;

    /// <summary>
    /// True while the thumb is held. A sample that lands mid-drag would snap it back to where the
    /// mixer was a moment ago, which feels like the control fighting the hand on it.
    /// </summary>
    private bool _grabbed;

    private bool _captionVisible = true;
    private bool _detached;

    /// <summary>
    /// The exact delegate instances the slider was hooked with. <c>RemoveHandler</c> matches on the
    /// delegate, so building a fresh one at teardown would unhook nothing and leave a torn-down
    /// view listening to a control that outlives it.
    /// </summary>
    private readonly PointerEventHandler _grab;

    private readonly PointerEventHandler _release;

    public VolumeView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var definition = WidgetCatalog.Definition(WidgetKind.Volume);

        _readout.Text = "--";
        _readout.Foreground = _readingBrush;
        _readout.VerticalAlignment = VerticalAlignment.Bottom;

        // Lifted off the baseline it sits beside: bottom-aligning a 12px caption against a 68px
        // number puts it below the number's baseline, which reads as a slipped line.
        _caption.VerticalAlignment = VerticalAlignment.Bottom;
        _caption.Margin = new Thickness(0, 0, 0, Fluent.Space1);

        // A Grid rather than a horizontal StackPanel: a StackPanel measures its children with
        // unbounded width, so the caption never learns how much room it has and its trimming never
        // fires — on a narrow tile it runs off the edge of the card instead of ellipsising.
        _reading.ColumnSpacing = Fluent.Space2;
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _reading.Margin = WidgetLayout.SideInset;
        _reading.VerticalAlignment = VerticalAlignment.Center;

        SetColumn(_readout, 0);
        SetColumn(_caption, 1);
        _reading.Children.Add(_readout);
        _reading.Children.Add(_caption);

        _muteIcon = Fluent.Icon(LevelGlyphs[(int)VolumeLevel.High], "TextFillColorPrimaryBrush");
        _mute.Content = _muteIcon;
        _mute.Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1);
        _mute.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _mute.VerticalAlignment = VerticalAlignment.Center;
        _mute.Click += OnMuteClicked;
        ToolTipService.SetToolTip(_mute, "Mute");

        _slider.Minimum = 0;
        _slider.Maximum = 100;
        _slider.StepFrequency = 1;
        _slider.VerticalAlignment = VerticalAlignment.Center;
        _slider.HorizontalAlignment = HorizontalAlignment.Stretch;
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

        SetColumn(_mute, 0);
        SetColumn(_slider, 1);
        _band.Children.Add(_mute);
        _band.Children.Add(_slider);
        SetRow(_band, 1);

        var message = new TextBlock { Text = "No output device" };
        _empty = Fluent.EmptyState(definition.Glyph, message, out _);
        _empty.Visibility = Visibility.Collapsed;

        Children.Add(_reading);
        Children.Add(_band);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as VolumeConfig ?? new VolumeConfig();

        UpdateVisibility();

        // Subscribing fires a tick of its own, which samples with the new config already in place.
        if (!Resubscribe())
        {
            Present(_current);
        }
    }

    /// <summary>
    /// Rebuild the ticker subscription only when the cadence actually changed.
    /// <para>
    /// Tearing it down and remaking it on every config touch made dragging a settings slider
    /// stutter, because each frame rebuilt the shared timer for every widget on the desktop.
    /// </para>
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
        _subscription = _ticker?.Subscribe(interval, Sample);
        return _subscription is not null;
    }

    private void Sample()
    {
        if (_detached)
        {
            return;
        }

        Present(AudioEndpoint.Read());
    }

    private void Present(VolumeReading reading)
    {
        _current = reading;

        UpdateVisibility();

        if (!reading.Available)
        {
            _readout.Text = "--";
            _readout.Foreground = _mutedBrush;
            _caption.Text = "Unavailable";
            return;
        }

        _readout.Text = VolumeRules.Readout(reading.Scalar);
        _caption.Text = VolumeRules.Caption(reading.Scalar, reading.Muted);

        // Muted is a colour, not an opacity: the disabled text brush is what every other disabled
        // thing on the desktop uses and it follows the theme.
        _readout.Foreground = reading.Muted ? _mutedBrush : _readingBrush;

        var level = VolumeRules.Level(reading.Scalar, reading.Muted);
        _muteIcon.Glyph = LevelGlyphs[(int)level];
        ToolTipService.SetToolTip(_mute, reading.Muted ? "Unmute" : "Mute");

        // Echoing: this is the poll writing back what it just read, and the ValueChanged it raises
        // must not be mistaken for somebody moving the control.
        if (!_grabbed)
        {
            _echoing = true;
            _slider.Value = VolumeRules.Percent(reading.Scalar);
            _echoing = false;
        }

        ApplyTypeScale();
    }

    /// <summary>
    /// Which parts are on screen. One place, so the three things that move them — the config, an
    /// endpoint that cannot be read, and the tile's own size — cannot disagree.
    /// </summary>
    private void UpdateVisibility()
    {
        var absent = !_current.Available;
        var showBand = _config.ShowSlider && !absent;

        _empty.Visibility = absent ? Visibility.Visible : Visibility.Collapsed;
        _reading.Visibility = absent ? Visibility.Collapsed : Visibility.Visible;
        _band.Visibility = showBand ? Visibility.Visible : Visibility.Collapsed;

        // Read-only is a state of the controls, not a reason to hide them: a greyed slider still
        // shows the level, and a tile that drops its slider when control is switched off looks
        // like a different widget.
        _slider.IsEnabled = _config.AllowControl;
        _mute.IsEnabled = _config.AllowControl;

        _caption.Visibility = _captionVisible && !absent ? Visibility.Visible : Visibility.Collapsed;

        ApplyTypeScale();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var wanted = args.NewSize.Width >= CaptionMinimumWidth;

        if (wanted != _captionVisible)
        {
            _captionVisible = wanted;
        }

        UpdateVisibility();
    }

    /// <summary>
    /// Step the readout to the tile: Subtitle, then Title, then larger as the tile grows.
    /// <para>
    /// A step, not a fraction of the tile. Text that is not on one of Windows' own sizes looks
    /// foreign next to everything else on the desktop, however carefully it was interpolated.
    /// </para>
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth - (Fluent.Space4 * 2);

        // What the number gets once the slider band and the gaps have taken theirs.
        var chrome = Fluent.Space4
            + (_band.Visibility == Visibility.Visible ? Fluent.RowHeightCompact + Fluent.Space4 : Fluent.Space4);

        // The caption shares the line, so the number gets at most half of it. Letting it take the
        // whole line is how a narrow tile ends up with a 68px number and a caption trimmed to one
        // ellipsis.
        var readingWidth = _caption.Visibility == Visibility.Visible ? width / 2 : width;

        // Four characters: "100%" is the widest this readout can ever be. Fitting to the live text
        // would make "9%" and "100%" pick different steps, so the number would change size between
        // two samples a second apart.
        var hero = FluentType.Hero(
            readingWidth,
            Math.Min(ActualHeight - chrome, FluentType.Display.LineHeight),
            4);

        if (hero.FontSize < FluentType.Subtitle.FontSize)
        {
            hero = FluentType.Subtitle;
        }

        var caption = hero.FontSize >= FluentType.Title.FontSize ? FluentType.Body : FluentType.Caption;

        if (hero != _heroStep)
        {
            _heroStep = hero;
            _readout.Apply(hero);
        }

        if (caption != _captionStep)
        {
            _captionStep = caption;
            _caption.Apply(caption);
        }
    }

    private void OnGrab(object sender, PointerRoutedEventArgs args) => _grabbed = true;

    private void OnRelease(object sender, PointerRoutedEventArgs args)
    {
        _grabbed = false;

        // Reconcile immediately rather than waiting out the interval: the endpoint may have
        // clamped or quantised what the drag asked for, and the tile should show what the machine
        // did, not what it was told.
        Sample();
    }

    private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_echoing || _detached || !_config.AllowControl)
        {
            return;
        }

        var scalar = VolumeRules.Scalar(args.NewValue);

        if (!AudioEndpoint.SetScalar(scalar))
        {
            // The endpoint refused. Put the control back where the machine actually is rather than
            // leaving it showing a level nothing is playing at.
            Sample();
            return;
        }

        // Moving a muted endpoint above silence unmutes it, which is what the volume flyout does
        // and what anybody dragging a slider expects.
        if (_current.Muted && scalar > 0)
        {
            AudioEndpoint.SetMute(false);
        }

        Sample();
    }

    private void OnMuteClicked(object sender, RoutedEventArgs args)
    {
        if (_detached || !_config.AllowControl || !_current.Available)
        {
            return;
        }

        AudioEndpoint.SetMute(!_current.Muted);
        Sample();
    }

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;
        _slider.ValueChanged -= OnSliderChanged;
        _slider.RemoveHandler(PointerPressedEvent, _grab);
        _slider.RemoveHandler(PointerReleasedEvent, _release);
        _slider.RemoveHandler(PointerCaptureLostEvent, _release);
        _slider.RemoveHandler(PointerCanceledEvent, _release);
        _mute.Click -= OnMuteClicked;

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;
        _ticker = null;
    }

    private static string From(int codePoint) => char.ConvertFromUtf32(codePoint);
}
