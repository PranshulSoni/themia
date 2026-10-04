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
/// <para>
/// [S-665] Its disclosure ladder, smallest tile first: the time; the date (short when narrow,
/// with the week appended on a wide strip); the week and day of the year once the tile is
/// Expanded in height; the time zone once it is Expanded both ways. A tall column stacks the hours
/// over the minutes, which is the only way its width lets the digits grow. Whatever the rungs
/// leave, the time fills.
/// </para>
/// </summary>
public sealed partial class ClockView : UserControl, IWidgetView, IWidgetSizeAware
{
    /// <summary>The longest long date: "Wednesday, 30 September".</summary>
    private const int LongDateCharacters = 23;

    /// <summary>The short date: "Wed 30 Sep".</summary>
    private const int ShortDateCharacters = 10;

    /// <summary>The long date with the week after it: "Wednesday, 30 September · Week 40 · Day 274".</summary>
    private const int DateWithWeekCharacters = 44;

    /// <summary>The longer of the detail lines: "UTC+02:00 · W. Europe Daylight Time".</summary>
    private const int DetailCharacters = 34;

    /// <summary>Line height of stacked digits, as a multiple of their size.</summary>
    private const double StackedLeading = 1.05;

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

    // The last area the card gave the view, and the rungs it settled on there. Kept so a config
    // change or a tick can lay out and render against them without waiting for a resize.
    private WidgetArea _area;
    private bool _stacked;
    private bool _shortDate;
    private bool _dateWithWeek;
    private bool _showWeek;
    private bool _showZone;

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
        WeekText.Margin = new Thickness(0, Fluent.Space1, 0, 0);
        ZoneText.Margin = new Thickness(0, Fluent.Space1, 0, 0);

        // Block line height, so a stacked readout's lines sit exactly one ramp line apart rather
        // than at the font's own (looser) spacing.
        TimeText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        TimeText.TextAlignment = TextAlignment.Center;

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
        MeridiemText.Visibility = _config.Use24Hour ? Visibility.Collapsed : Visibility.Visible;

        if (_secondHand is not null)
        {
            _secondHand.Visibility = _config.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        }

        Lay();
        LayoutAnalog();
        Resubscribe();
    }

    /// <summary>[S-663] The card's word on how much room the clock has.</summary>
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
    }

    /// <summary>
    /// Climb the ladder for the current area, then give the time everything the rungs left.
    /// <para>
    /// The size classes decide which rungs are considered — a week line on a 150-pixel tile would be
    /// taking room from the one thing it is for — and <see cref="Disclosure"/> decides which of those
    /// actually fit, so a rung is never drawn clipped. Every size is a step of the Windows 11 ramp, or
    /// for a tile larger than the ramp, a multiple of it that keeps Fluent's line-height ratio.
    /// </para>
    /// </summary>
    private void Lay()
    {
        var box = _area.Deflate(Root.Padding.Left, Root.Padding.Top);
        var width = box.Width;
        var height = box.Height;

        var room = new Disclosure(box, Fluent.Space1);

        // The time at its smallest is always there; the rungs share what it leaves.
        room.Reserve(FluentType.Subtitle.LineHeight);

        // The date: the long one when the line can hold it, the short one when it cannot, and on a
        // wide strip — which has width it cannot otherwise use — the week after it.
        _dateWithWeek = _area.Shape == AreaShape.Wide && _area.WidthClass >= SizeClass.Expanded;
        _shortDate = !_dateWithWeek && width < FluentType.EstimateWidth(FluentType.Caption, LongDateCharacters);
        var dateCharacters = _dateWithWeek ? DateWithWeekCharacters : _shortDate ? ShortDateCharacters : LongDateCharacters;

        // Support, never a second headline: capped at BodyLarge however large the tile grows (the
        // steps above it are semibold and would read as a second title).
        var dateStep = FluentType.Fit(width, height * 0.22, dateCharacters, FluentType.Caption, FluentType.BodyLarge);
        var showDate = _config.ShowDate && room.Take(dateStep.LineHeight);

        // The details are a step under the date, so the eye reads time, date, then the rest.
        var detailStep = FluentType.Fit(width, height * 0.12, DetailCharacters, FluentType.Caption, FluentType.Body);
        _showWeek = showDate && !_dateWithWeek && _area.HeightClass >= SizeClass.Expanded && room.Take(detailStep.LineHeight);
        _showZone = _area.AtLeast(SizeClass.Expanded, SizeClass.Expanded) && room.Take(detailStep.LineHeight);

        DateText.Visibility = showDate ? Visibility.Visible : Visibility.Collapsed;
        WeekText.Visibility = _showWeek ? Visibility.Visible : Visibility.Collapsed;
        ZoneText.Visibility = _showZone ? Visibility.Visible : Visibility.Collapsed;

        DateText.Apply(dateStep);
        WeekText.Apply(detailStep);
        ZoneText.Apply(detailStep);

        // The time has what the rungs left, plus the minimum it reserved for itself.
        var timeHeight = room.Remaining + FluentType.Subtitle.LineHeight;

        // "12:34:56" plus " PM" — the meridiem sits on the same line, so it counts towards the width.
        var meridiem = _config.Use24Hour ? 0 : 3;
        var characters = (_config.ShowSeconds ? 8 : 5) + meridiem;
        var timeStep = FluentType.Hero(width, timeHeight, characters);

        // A tall column is too narrow for "15:40" to be large on one line; the hours over the minutes
        // (over the seconds) use its height instead — only when that actually draws them larger.
        // Stacked digits are set solid (StackedLeading) rather than at the ramp's 1.3 leading: two
        // lines of figures with a ramp line's air between them read as two separate numbers.
        var lines = _config.ShowSeconds ? 3 : 2;
        var perLine = timeHeight / lines;
        var byWidth = FluentType.Hero(width, perLine * 2, 2 + meridiem);
        var stackedSize = Math.Min(byWidth.FontSize, perLine / StackedLeading);
        var stackedStep = new TypeStep(stackedSize, stackedSize * StackedLeading, true);
        _stacked = _area.Shape == AreaShape.Tall && stackedStep.FontSize > timeStep.FontSize * 1.2;

        if (_stacked)
        {
            timeStep = stackedStep;
        }

        TimeText.Apply(timeStep);

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

        Render();
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
            TimeText.Text = _stacked ? reading.Time.Replace(':', '\n') : reading.Time;
            MeridiemText.Text = reading.Meridiem ?? string.Empty;

            // The extras are worked out only when a rung that shows one is up.
            if (_shortDate || _dateWithWeek || _showWeek || _showZone)
            {
                var details = ClockFormatter.Details(DateTimeOffset.UtcNow, _config);
                DateText.Text = _shortDate ? details.ShortDate
                    : _dateWithWeek ? $"{reading.Date} · {details.Week}"
                    : reading.Date ?? string.Empty;
                WeekText.Text = details.Week;
                ZoneText.Text = details.Zone;
            }
            else
            {
                DateText.Text = reading.Date ?? string.Empty;
            }

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
