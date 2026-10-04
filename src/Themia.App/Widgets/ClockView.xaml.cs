using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// The clock. Subscribes to the shared <see cref="Ticker"/> at the cadence its config actually
/// needs, and unsubscribes when it leaves the tree — a widget that is not on screen must not keep
/// the process waking up.
/// </summary>
public sealed partial class ClockView : UserControl, IWidgetView
{
    /// <summary>
    /// One device-independent pixel. The rim and the ticks are drawn at this thickness at the
    /// tile's real scale, which is the whole reason the analog face is no longer inside a Viewbox:
    /// a hairline that has been scaled up is not a hairline.
    /// </summary>
    private const double Hairline = 1;

    private Ticker? _ticker;
    private ClockConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    // The analog face is rebuilt when its box changes size, so the hands' transforms and the
    // second hand are held here rather than in the XAML.
    private RotateTransform _hourRotation = new();
    private RotateTransform _minuteRotation = new();
    private RotateTransform _secondRotation = new();
    private Rectangle? _secondHand;
    private double _analogDiameter;

    public ClockView()
    {
        InitializeComponent();

        // The card no longer pads its content; the clock carries its own inset. [S-645] The full
        // inset either side, but only half of it above and below: the readout is centred and sized
        // to its line height, which already carries its own leading, and the card's header leaves
        // a gap under itself. A full 16px top and bottom cost a wide strip a third of the height
        // the digits could have had.
        Root.Padding = new Thickness(WidgetLayout.ContentInset, Fluent.Space2, WidgetLayout.ContentInset, Fluent.Space2);
        TimeRow.Spacing = Fluent.Space1;
        DateText.Margin = new Thickness(0, Fluent.Space1, 0, 0);

        Root.SizeChanged += (_, _) => ApplyTypeScale();

        // Its own handler rather than Root's: the analog box is only measured once Root has been
        // arranged, so reading its size from Root's SizeChanged would read the previous one.
        AnalogRoot.SizeChanged += (_, _) => LayoutAnalog();

        Unloaded += (_, _) => Detach();
    }

    /// <summary>Wire the widget to its data. Called once when the card is built.</summary>
    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    /// <summary>Re-read the config after the user changed it, resubscribing if the cadence changed.</summary>
    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ClockConfig ?? new ClockConfig();

        DigitalRoot.Visibility = _config.Face == ClockFace.Digital ? Visibility.Visible : Visibility.Collapsed;
        AnalogRoot.Visibility = _config.Face == ClockFace.Analog ? Visibility.Visible : Visibility.Collapsed;
        DateText.Visibility = _config.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        MeridiemText.Visibility = _config.Use24Hour ? Visibility.Collapsed : Visibility.Visible;

        if (_secondHand is not null)
        {
            _secondHand.Visibility = _config.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        }

        ApplyTypeScale();
        LayoutAnalog();
        Resubscribe();
    }

    /// <summary>
    /// Pick the ramp steps the readout is drawn at, so the clock fills whatever box it was given.
    /// <para>
    /// The time is measured against both axes: against width because a long string like
    /// <c>12:34:56 PM</c> needs far more of it than <c>09:05</c>, and against height because a wide,
    /// short tile would otherwise clip. The date takes its share out of the height first, so adding
    /// it shrinks the time rather than pushing it out of the tile.
    /// </para>
    /// <para>
    /// Every size here is a step of the Windows 11 ramp — or, for a tile large enough that the ramp
    /// has nothing big enough to say, a multiple of its Display step that keeps Fluent's own
    /// line-height ratio. Nothing is a fraction of the tile any more.
    /// </para>
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = Root.ActualWidth - Root.Padding.Left - Root.Padding.Right;
        var height = Root.ActualHeight - Root.Padding.Top - Root.Padding.Bottom;

        // "12:34:56" plus " PM" — the meridiem sits on the same line, so it counts towards the width
        // the time has to fit in.
        var characters = (_config.ShowSeconds ? 8 : 5) + (_config.Use24Hour ? 0 : 3);

        // The date is support, never a second headline: it is capped at BodyLarge however large the
        // tile grows — the steps above it are semibold and would read as a second title — and it
        // takes its room out of the height before the time is sized.
        var dateStep = _config.ShowDate
            ? FluentType.Fit(width, height * 0.22, 16, FluentType.Caption, FluentType.BodyLarge)
            : FluentType.Caption;

        var dateBlock = _config.ShowDate ? dateStep.LineHeight + Fluent.Space1 : 0;

        var timeStep = FluentType.Hero(width, height - dateBlock, characters);

        TimeText.Apply(timeStep);
        DateText.Apply(dateStep);

        // The meridiem is the largest ramp step that stays well under the readout: a clock whose
        // "PM" is as tall as its digits reads as two numbers.
        var meridiemStep = FluentType.Fit(
            width,
            timeStep.FontSize * 0.45,
            2,
            FluentType.Caption,
            FluentType.Title);

        MeridiemText.Apply(meridiemStep);

        // Both blocks are bottom-aligned, so lifting the meridiem by the difference in half-leading
        // sits its glyphs on the readout's baseline instead of under it.
        var lift = HalfLeading(timeStep) - HalfLeading(meridiemStep);
        MeridiemText.Margin = new Thickness(0, 0, 0, Math.Max(0, lift));
    }

    private static double HalfLeading(TypeStep step) => (step.LineHeight - step.FontSize) / 2;

    // -----------------------------------------------------------------------------------------
    // Analog face
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Rebuild the analog face when the box it lives in changed size. A resize, not a tick — the
    /// once-a-second path only moves three transforms.
    /// </summary>
    private void LayoutAnalog()
    {
        if (_config.Face != ClockFace.Analog)
        {
            return;
        }

        var diameter = Math.Floor(Math.Min(AnalogRoot.ActualWidth, AnalogRoot.ActualHeight));

        // A box that has not been measured, or one too small for a readable dial, gets no face at
        // all rather than a smear of overlapping hairlines.
        if (!double.IsFinite(diameter) || diameter < 24)
        {
            AnalogFace.Children.Clear();
            _secondHand = null;
            _analogDiameter = 0;
            return;
        }

        if (Math.Abs(diameter - _analogDiameter) < 1)
        {
            return;
        }

        _analogDiameter = diameter;
        BuildAnalogFace(diameter);
        Render();
    }

    private void BuildAnalogFace(double diameter)
    {
        AnalogFace.Children.Clear();
        AnalogFace.Width = diameter;
        AnalogFace.Height = diameter;

        var radius = diameter / 2;

        var rim = FluentLayout.Themed("ControlStrokeColorDefaultBrush", Colors.Gray);
        var hourTick = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        var minuteTick = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        var hand = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        var accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

        var dial = new Ellipse
        {
            Width = diameter - Hairline,
            Height = diameter - Hairline,
            Stroke = rim,
            StrokeThickness = Hairline,
        };

        Canvas.SetLeft(dial, Hairline / 2);
        Canvas.SetTop(dial, Hairline / 2);
        AnalogFace.Children.Add(dial);

        // Sixty marks is the real Windows dial, but under about 160 DIPs they are closer together
        // than they are long, so a small face keeps the twelve that actually tell the time.
        var minutes = diameter >= 160;
        var outer = radius - (Hairline * 2);

        for (var mark = 0; mark < 60; mark++)
        {
            var onHour = mark % 5 == 0;

            if (!onHour && !minutes)
            {
                continue;
            }

            var angle = mark * 6 * Math.PI / 180;
            var length = onHour ? Math.Max(3, radius * 0.10) : Math.Max(1.5, radius * 0.04);
            var inner = outer - length;

            AnalogFace.Children.Add(new Line
            {
                X1 = radius + (Math.Sin(angle) * inner),
                Y1 = radius - (Math.Cos(angle) * inner),
                X2 = radius + (Math.Sin(angle) * outer),
                Y2 = radius - (Math.Cos(angle) * outer),
                Stroke = onHour ? hourTick : minuteTick,
                StrokeThickness = Hairline,
            });
        }

        // Fresh transforms each rebuild: the previous hands are unparented but still hold a
        // reference to the old ones, and a transform with two owners is a rotation applied twice.
        _hourRotation = new RotateTransform();
        _minuteRotation = new RotateTransform();
        _secondRotation = new RotateTransform();

        AddHand(hand, radius * 0.50, Math.Max(2, radius * 0.042), 0, _hourRotation, radius);
        AddHand(hand, radius * 0.74, Math.Max(1.5, radius * 0.030), 0, _minuteRotation, radius);

        _secondHand = AddHand(accent, radius * 0.80, Math.Max(1, radius * 0.014), radius * 0.16, _secondRotation, radius);
        _secondHand.Visibility = _config.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;

        var hubSize = Math.Max(4, radius * 0.10);

        var hub = new Ellipse
        {
            Width = hubSize,
            Height = hubSize,
            Fill = accent,
        };

        Canvas.SetLeft(hub, radius - (hubSize / 2));
        Canvas.SetTop(hub, radius - (hubSize / 2));
        AnalogFace.Children.Add(hub);
    }

    /// <summary>
    /// One hand: a rounded bar standing straight up from the centre, rotated about the point where
    /// it meets the hub. <paramref name="tail"/> is the stub that carries on past the centre, which
    /// only the second hand has.
    /// </summary>
    private Rectangle AddHand(
        Brush brush,
        double length,
        double thickness,
        double tail,
        RotateTransform rotation,
        double centre)
    {
        var bar = new Rectangle
        {
            Width = thickness,
            Height = length + tail,
            RadiusX = thickness / 2,
            RadiusY = thickness / 2,
            Fill = brush,
            RenderTransformOrigin = new Point(0.5, length / (length + tail)),
            RenderTransform = rotation,
        };

        Canvas.SetLeft(bar, centre - (thickness / 2));
        Canvas.SetTop(bar, centre - length);
        AnalogFace.Children.Add(bar);

        return bar;
    }

    // -----------------------------------------------------------------------------------------
    // Ticking
    // -----------------------------------------------------------------------------------------

    private void Resubscribe()
    {
        // Only when the cadence actually changed. Dropping and remaking the subscription on every
        // config touch is what made dragging a settings slider stutter: each tick tore down the
        // timer subscription for every widget on the desktop and built it again.
        var wanted = _config.TickInterval;
        if (_subscription is not null && wanted == _subscribedInterval)
        {
            Render();
            return;
        }

        _subscription?.Dispose();
        _subscribedInterval = wanted;
        _subscription = _ticker?.Subscribe(wanted, Render);
    }

    private void Render()
    {
        var reading = ClockFormatter.Read(DateTimeOffset.UtcNow, _config);

        if (_config.Face == ClockFace.Digital)
        {
            TimeText.Text = reading.Time;
            MeridiemText.Text = reading.Meridiem ?? string.Empty;
            DateText.Text = reading.Date ?? string.Empty;
            return;
        }

        _hourRotation.Angle = reading.HourAngle;
        _minuteRotation.Angle = reading.MinuteAngle;
        _secondRotation.Angle = reading.SecondAngle;
    }

    public void Detach()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
