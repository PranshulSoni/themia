using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Astronomy;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// Sunrise, sunset and the moon, computed offline from a configured latitude and longitude.
/// <para>
/// The drawing is the sun's <b>real</b> path — altitude against how far it is through its passage
/// across the sky — so it flattens in winter, towers in summer and barely lifts off the horizon
/// inside the polar circles. [S-167] Elapsed daylight is filled in behind the sun; the night stretch
/// carries on the same way round the sky, sets right, nadir, rises left, with its depth compressed
/// against the daylight scale because a true-to-scale winter midnight would be deeper than the
/// whole widget. [S-168]
/// </para>
/// <para>
/// The moon sits <i>beside</i> the graphic, never on the solar arc: the two do not share a track,
/// and drawing it there would imply they do. [S-169]
/// </para>
/// <para>
/// The drawing is a Windows control, not an illustration: the travelled arc and the daylight behind
/// it are the theme's accent, the untravelled arc and the night stretch are the theme's greys, the
/// horizon is a hairline in the divider colour, and the sun below the horizon changes to the
/// theme's <i>disabled</i> accent rather than being faded with an opacity. Nothing here names a
/// colour of its own, so the whole graphic follows light, dark and the high-contrast themes.
/// </para>
/// <para>
/// All the astronomy lives in <see cref="Themia.Core.Astronomy"/> as pure functions of an instant,
/// so this file only turns numbers into shapes.
/// </para>
/// </summary>
internal sealed partial class SunMoonView : Grid, IWidgetView, IWidgetSurfaceAware
{
    /// <summary>[S-170] The graphic is never dropped; below this it is simply drawn smaller.</summary>
    private const double GraphicFloor = 46;

    /// <summary>Under either of these the rows go compact: Windows' 32px list row. </summary>
    private const double CompactWidth = 260;

    private const double CompactHeight = 220;

    private readonly Canvas _canvas = new();
    private readonly Polygon _elapsedFill = new();
    private readonly Polyline _dayTrack = new();
    private readonly Polyline _nightTrack = new();
    private readonly Polyline _doneTrack = new();
    private readonly Line _horizon = new();
    private readonly Ellipse _sun = new();

    private readonly Grid _strip;
    private readonly StripLabel _riseLabel;
    private readonly StripLabel _dayLabel;
    private readonly StripLabel _setLabel;

    private readonly MoonVisual _moonBadge;
    private readonly TextBlock _moonPercent = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _moonPanel;

    private readonly Grid _body;
    private readonly StackPanel _rowList = new() { Spacing = 0 };
    private readonly EventRow _sunriseRow;
    private readonly EventRow _sunsetRow;
    private readonly EventRow _daylightRow;
    private readonly EventRow _moonRow;
    private readonly MoonVisual _moonGlyph;

    /// <summary>The accent, as a fill. The travelled arc, the daylight behind it and the sun.</summary>
    private readonly Brush _accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

    /// <summary>The theme's own dimmed accent, for the sun once it is under the horizon.</summary>
    private readonly Brush _accentDim = FluentLayout.Themed("AccentFillColorDisabledBrush", Colors.SlateGray);

    private readonly Brush _tertiary = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
    private readonly Brush _disabled = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.DimGray);
    private readonly Brush _divider = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray);

    private SunMoonConfig _config = new();

    /// <summary>The card this view draws on, which owns the header it names.</summary>
    private IWidgetSurface? _surface;
    private Ticker? _ticker;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private DateTimeOffset _now = DateTimeOffset.Now;

    public SunMoonView()
    {
        BuildCanvas();

        _riseLabel = new StripLabel(Glyphs.Sun);
        _dayLabel = new StripLabel(Glyphs.Clock);
        _setLabel = new StripLabel(Glyphs.Sun);
        _strip = BuildStrip();

        _moonBadge = new MoonVisual();
        _moonGlyph = new MoonVisual();
        _moonPanel = BuildMoonPanel();

        _sunriseRow = new EventRow(Fluent.Icon(Glyphs.Sun), "Sunrise");
        _sunsetRow = new EventRow(Fluent.Icon(Glyphs.Sun), "Sunset");
        _daylightRow = new EventRow(Fluent.Icon(Glyphs.Clock), "Daylight");
        _moonRow = new EventRow(_moonGlyph.Root, "Moon");

        _rowList.Margin = new Thickness(0, 0, 0, Fluent.Space2);
        _rowList.Children.Add(_sunriseRow.Root);
        _rowList.Children.Add(_sunsetRow.Root);
        _rowList.Children.Add(_daylightRow.Root);
        _rowList.Children.Add(_moonRow.Root);

        // The graphic and its number strip share a column; the moon takes the one beside it, so it
        // can never be drawn on top of the sun's arc. [S-169]
        var graphicColumn = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        SetRow(_canvas, 0);
        SetRow(_strip, 1);
        graphicColumn.Children.Add(_canvas);
        graphicColumn.Children.Add(_strip);

        _body = new Grid
        {
            // The rows carry the inset as their own padding, so only the parts above them take it
            // as a margin; that is what lets a row highlight reach both edges of the widget.
            Margin = new Thickness(Fluent.Space4, 0, Fluent.Space4, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        SetColumn(graphicColumn, 0);
        SetColumn(_moonPanel, 1);
        _body.Children.Add(graphicColumn);
        _body.Children.Add(_moonPanel);

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        SetRow(_body, 0);
        SetRow(_rowList, 1);

        Children.Add(_body);
        Children.Add(_rowList);

        SizeChanged += (_, _) => ApplyLayout();
        _canvas.SizeChanged += (_, _) => Redraw();
    }

    private void BuildCanvas()
    {
        // The elapsed daylight is the accent behind the curve. The alpha is the shape's, never the
        // shared brush's: the brush comes out of the application's resources and is the same object
        // every other accent surface in the app is painted with.
        _elapsedFill.Fill = _accent;
        _elapsedFill.Opacity = Fluent.ChartWash;

        // The stretch still to come: present, but plainly secondary.
        _dayTrack.Stroke = _tertiary;
        _dayTrack.StrokeThickness = Fluent.Hairline;
        _dayTrack.StrokeLineJoin = PenLineJoin.Round;

        // Under the horizon. Dashed and in the theme's disabled text grey rather than a faded copy
        // of the day track.
        _nightTrack.Stroke = _disabled;
        _nightTrack.StrokeThickness = Fluent.Hairline;
        _nightTrack.StrokeDashArray = [3, 3];

        // Where the sun has already been, in the accent: the one emphasised line in the drawing.
        _doneTrack.Stroke = _accent;
        _doneTrack.StrokeThickness = Fluent.ChartStroke;
        _doneTrack.StrokeLineJoin = PenLineJoin.Round;

        // A hairline in the colour Windows rules with, not a faded text colour.
        _horizon.Stroke = _divider;
        _horizon.StrokeThickness = Fluent.Hairline;

        _sun.Fill = _accent;

        _canvas.MinHeight = GraphicFloor;
        _canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
        _canvas.VerticalAlignment = VerticalAlignment.Stretch;
        _canvas.IsHitTestVisible = false;

        _canvas.Children.Add(_elapsedFill);
        _canvas.Children.Add(_dayTrack);
        _canvas.Children.Add(_nightTrack);
        _canvas.Children.Add(_doneTrack);
        _canvas.Children.Add(_horizon);
        _canvas.Children.Add(_sun);
    }

    /// <summary>
    /// The number strip. Its three columns put each label on the gridline of the curve point it
    /// names — 13%, 50% and 87%, the same anchors the plot uses for sunrise, noon and sunset.
    /// </summary>
    private Grid BuildStrip()
    {
        var strip = new Grid
        {
            Margin = new Thickness(0, Fluent.Space1, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(0.26, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(0.48, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(0.26, GridUnitType.Star) },
            },
        };

        SetColumn(_riseLabel.Root, 0);
        SetColumn(_dayLabel.Root, 1);
        SetColumn(_setLabel.Root, 2);

        strip.Children.Add(_riseLabel.Root);
        strip.Children.Add(_dayLabel.Root);
        strip.Children.Add(_setLabel.Root);

        return strip;
    }

    private StackPanel BuildMoonPanel()
    {
        _moonPercent.HorizontalAlignment = HorizontalAlignment.Center;

        return new StackPanel
        {
            Spacing = Fluent.Space1,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(Fluent.Space3, 0, 0, 0),
            Children = { _moonBadge.Root, _moonPercent },
        };
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;
        PublishTitle();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as SunMoonConfig ?? new SunMoonConfig();
        PublishTitle();

        Resubscribe();

        // ApplyLayout ends in a Refresh, so the readout and the drawing pick the new config up in
        // the same pass rather than in two.
        ApplyLayout();
    }

    private void Resubscribe()
    {
        var interval = _config.TickInterval;

        // Only when the cadence actually changed. Dropping and remaking the subscription on every
        // config touch made dragging a settings slider stutter, because each frame tore down the
        // timer subscription for every widget on the desktop and built it again.
        if (_subscription is not null && interval == _subscribedInterval)
        {
            return;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Tick);
    }

    private void Tick()
    {
        _now = DateTimeOffset.Now;
        Refresh();
    }

    public void Detach()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    /// <summary>
    /// What is shown at this size. [S-170]
    /// <para>
    /// The graphic is never dropped — the user asked for it — so only extras shed as the tile
    /// shrinks: first the countdown, then the label icons and the moon's percentage, then the
    /// daylight label, then the number strip.
    /// </para>
    /// <para>
    /// How many rows appear is no longer a set of height thresholds picked by hand: rows are a fixed
    /// 40px, or 32 on a small tile, so the widget simply works out how many of that height are left
    /// once the graphic's floor has taken its share, and shows that many — moon first to go, then
    /// daylight, then the two events. With the graphic hidden all four stay, since a widget showing
    /// nothing is never the answer.
    /// </para>
    /// </summary>
    private void ApplyLayout()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        var graphic = _config.PathMode != SunPathMode.Hidden;

        // The two settings are independent, with one guard: with the graphic hidden the rows are
        // forced on. [S-170]
        var rows = _config.ShowEvents || !graphic;
        var numbers = graphic && _config.PathMode == SunPathMode.Numbers;

        var compact = width > 0 && height > 0 && (width < CompactWidth || height < CompactHeight);
        var rowHeight = compact ? Fluent.RowHeightCompact : Fluent.RowHeight;

        // The thresholds are the width the shed-able parts need at their ramp step, so nothing ever
        // has to be clipped to fit.
        var showStrip = numbers && height >= 80 && width >= 120;
        var showCountdown = width >= 190 && height >= 120;
        var showDaylightLabel = width >= 140;
        var showSmallExtras = width >= 160;

        // What is left for rows once the graphic's floor has taken its share. The header belongs to
        // the card and sits outside this box, so it takes nothing out of it.
        var chrome = Fluent.Space4 + Fluent.Space2 + (graphic ? GraphicFloor : 0);

        var room = height - chrome;
        var fit = room > 0 ? (int)Math.Floor(room / rowHeight) : 0;

        var showEventRows = rows && (!graphic || fit >= 2);
        var showDaylightRow = rows && (!graphic || fit >= 3);
        var showMoonRow = rows && (!graphic || fit >= 4);

        _canvas.Visibility = Show(graphic);
        _moonPanel.Visibility = Show(graphic);
        _body.Visibility = Show(graphic);

        // Under a graphic the rows sit at the bottom; on their own they sit at the top, because
        // nothing above them is pushing them down.
        RowDefinitions[0].Height = graphic ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        RowDefinitions[1].Height = graphic ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _rowList.VerticalAlignment = graphic ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _strip.Visibility = Show(showStrip);
        _rowList.Visibility = Show(showEventRows || showDaylightRow || showMoonRow);

        _sunriseRow.Root.Visibility = Show(showEventRows);
        _sunsetRow.Root.Visibility = Show(showEventRows);
        _daylightRow.Root.Visibility = Show(showDaylightRow);
        _moonRow.Root.Visibility = Show(showMoonRow);

        _sunriseRow.SetDensity(compact);
        _sunsetRow.SetDensity(compact);
        _daylightRow.SetDensity(compact);
        _moonRow.SetDensity(compact);

        _moonPercent.Visibility = Show(graphic && numbers && showSmallExtras);
        _dayLabel.Root.Visibility = Show(showDaylightLabel);

        _riseLabel.ShowIcon(showSmallExtras);
        _dayLabel.ShowIcon(showSmallExtras);
        _setLabel.ShowIcon(showSmallExtras);

        _riseLabel.AllowCountdown = showCountdown;
        _dayLabel.AllowCountdown = false;
        _setLabel.AllowCountdown = showCountdown;

        _sunriseRow.AllowCountdown = showCountdown;
        _sunsetRow.AllowCountdown = showCountdown;

        // The graphic keeps its floor but gives the rest of the tile back when the rows are hidden.
        _canvas.MinHeight = graphic ? GraphicFloor : 0;

        ApplyType(width, height);
        Refresh();
    }

    /// <summary>
    /// Pick the ramp steps and the drawn sizes for this tile.
    /// <para>
    /// Only the number strip actually scales, because it is the graphic's readout and a large tile
    /// should say it loudly. The rows stay on their list-row steps at every size: Windows grows a
    /// list by showing more of it, not by growing its type.
    /// </para>
    /// </summary>
    private void ApplyType(double width, double height)
    {
        // Each strip column is 26% of the content width, and holds an icon, a time and a little
        // slack — roughly eight characters' worth.
        var column = Math.Max(0, (width - (Fluent.Space4 * 2)) * 0.26);
        var band = Math.Max(0, height - GraphicFloor - (Fluent.Space6 * 2));
        var value = FluentType.Strong(FluentType.Fit(column, band, 8, FluentType.Caption, FluentType.Subtitle));

        _riseLabel.SetStep(value);
        _dayLabel.SetStep(value);
        _setLabel.SetStep(value);

        // The badge beside the graphic is the one drawn thing that grows, and it grows on the 8px
        // grid rather than by a fraction of the tile.
        _moonBadge.Resize(BadgeSize(width, height));
    }

    private static double BadgeSize(double width, double height)
    {
        if (width >= 420 && height >= 320)
        {
            return 40;
        }

        if (width >= 300 && height >= 220)
        {
            return 32;
        }

        return width >= 200 && height >= 150 ? 24 : 16;
    }

    /// <summary>Recompute the readout and redraw. Called on a tick, a config change and a resize.</summary>
    private void Refresh()
    {
        var latitude = _config.EffectiveLatitude;
        var longitude = _config.EffectiveLongitude;

        var times = SolarPosition.TimesFor(_now, latitude, longitude);
        var next = SolarPosition.NextEvent(_now, latitude, longitude);
        var moon = MoonPhase.At(_now);
        var moonUp = SkyPath.PositionOf(MoonPhase.SublunarPoint(_now), latitude, longitude).Up;

        var sunrise = Clock(times.Sunrise);
        var sunset = Clock(times.Sunset);
        var daylight = Daylight(times);
        var southern = latitude < 0;

        // The time remaining rides on whichever of the two rows is next, and that row is
        // emphasised. There is no separate banner.
        var countdown = next is { } evt ? Countdown(evt.At - _now) : null;
        var riseCountdown = next?.Kind == SunEventKind.Sunrise ? countdown : null;
        var setCountdown = next?.Kind == SunEventKind.Sunset ? countdown : null;

        _riseLabel.Set(sunrise, riseCountdown);
        _dayLabel.Set(daylight, null);
        _setLabel.Set(sunset, setCountdown);

        _sunriseRow.Set(sunrise, riseCountdown);
        _sunsetRow.Set(sunset, setCountdown);
        _daylightRow.Set(daylight, null);
        _moonRow.SetLabel(MoonPhase.Label(moon.Name));
        _moonRow.Set($"{(int)Math.Round(moon.Illumination * 100)}%", null);

        _moonPercent.Text = $"{(int)Math.Round(moon.Illumination * 100)}%";
        _moonBadge.Update(moon.Phase, southern, moonUp);
        _moonGlyph.Update(moon.Phase, southern, moonUp);

        Redraw();
    }

    /// <summary>
    /// Rebuild the drawing. Cheap by construction: six shapes and about a hundred points,
    /// recomputed only on a tick, a config change or a resize — never per frame.
    /// </summary>
    private void Redraw()
    {
        if (_canvas.Visibility != Visibility.Visible)
        {
            return;
        }

        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;

        if (!(width > 0) || !(height > 0))
        {
            return;
        }

        var latitude = _config.EffectiveLatitude;
        var longitude = _config.EffectiveLongitude;

        var ground = SolarPosition.SubsolarPoint(_now);
        var declination = ground.Latitude;
        var position = SkyPath.PositionOf(ground, latitude, longitude);
        var plot = SunPathPlot.For(width, height, latitude, declination);

        _dayTrack.Points = ToPoints(SunPathGeometry.Curve(SkyPath.Samples(declination, latitude, false), plot));
        _nightTrack.Points = ToPoints(SunPathGeometry.Curve(SkyPath.Samples(declination, latitude, true), plot));

        if (position.Up)
        {
            var elapsed = SkyPath.Samples(declination, latitude, false, 32, 0, position.Progress);
            _elapsedFill.Points = ToPoints(SunPathGeometry.Area(elapsed, plot));
            _doneTrack.Points = ToPoints(SunPathGeometry.Curve(elapsed, plot));
            _elapsedFill.Visibility = Visibility.Visible;
            _doneTrack.Visibility = Visibility.Visible;
        }
        else
        {
            _elapsedFill.Visibility = Visibility.Collapsed;
            _doneTrack.Visibility = Visibility.Collapsed;
        }

        _horizon.X1 = 0;
        _horizon.X2 = width;
        _horizon.Y1 = plot.HorizonY;
        _horizon.Y2 = plot.HorizonY;

        var marker = plot.PointAt(new SkySample(position.Progress, position.AltitudeDegrees));
        Place(_sun, marker, MarkerRadius(width, height));

        // The sun below the horizon is still drawn, in the theme's disabled accent, so the night
        // stretch reads as the same journey rather than as an empty track.
        _sun.Fill = position.Up ? _accent : _accentDim;

        // A marker at the edge of the plot would otherwise bleed over the rows beneath it.
        _canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
    }

    /// <summary>The sun's dot, in three steps rather than as a fraction of the canvas.</summary>
    private static double MarkerRadius(double width, double height)
    {
        var smaller = Math.Min(width, height);
        return smaller >= 160 ? 6 : smaller >= 96 ? 5 : 4;
    }

    private static void Place(Ellipse ellipse, PlotPoint centre, double radius)
    {
        ellipse.Width = radius * 2;
        ellipse.Height = radius * 2;
        Canvas.SetLeft(ellipse, centre.X - radius);
        Canvas.SetTop(ellipse, centre.Y - radius);
    }

    private static PointCollection ToPoints(IReadOnlyList<PlotPoint> points)
    {
        var collection = new PointCollection();

        foreach (var point in points)
        {
            collection.Add(new Point(point.X, point.Y));
        }

        return collection;
    }

    /// <summary>
    /// A wall-clock time for an instant. 24-hour and zero-padded, written out rather than formatted
    /// through a culture pattern: the app builds with <c>InvariantGlobalization</c>. An absent time
    /// is an em dash, because inside the polar circles there is no event to show. [S-161]
    /// </summary>
    private static string Clock(DateTimeOffset? moment)
        => moment is { } instant ? $"{instant.ToLocalTime():HH\\:mm}" : "—";

    private static string Daylight(SunTimes times)
    {
        if (times.Sunrise is not { } sunrise || times.Sunset is not { } sunset)
        {
            return times.Polar == PolarState.Day ? "24h" : "0h";
        }

        var minutes = (int)Math.Round((sunset - sunrise).TotalMinutes);
        return $"{minutes / 60}h {minutes % 60}m";
    }

    private static string Countdown(TimeSpan remaining)
    {
        var minutes = Math.Max(0, (int)Math.Round(remaining.TotalMinutes));
        return minutes >= 60 ? $"in {minutes / 60}h {minutes % 60}m" : $"in {minutes}m";
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The moon at its current phase. [S-169]
    /// <para>
    /// The lit limb comes from <see cref="MoonDisc"/>, so the badge beside the graphic and the glyph
    /// in the row list are the same drawing and cannot disagree about how much of the moon is lit.
    /// The unlit part keeps a hairline rim, so a thin crescent still reads as a moon rather than as
    /// a stray mark, and below the horizon the whole disc steps down a named grey rather than being
    /// faded with an opacity.
    /// </para>
    /// </summary>
    private sealed class MoonVisual
    {
        private readonly Ellipse _rim = new();
        private readonly Polygon _lit = new();

        private readonly Brush _litUp = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        private readonly Brush _litDown = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        private readonly Brush _rimUp = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        private readonly Brush _rimDown = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.DimGray);

        private double _size;
        private double _phase;
        private bool _southern;

        public MoonVisual()
        {
            _rim.StrokeThickness = Fluent.Hairline;
            _rim.Fill = null;
            _rim.Stroke = _rimUp;
            _lit.Fill = _litUp;

            Root = new Grid { Children = { _rim, _lit } };
            Resize(Fluent.IconSize);
        }

        public Grid Root { get; }

        public void Resize(double size)
        {
            if (Math.Abs(size - _size) < 0.5)
            {
                return;
            }

            _size = size;
            Root.Width = size;
            Root.Height = size;
            _rim.Width = size - 1;
            _rim.Height = size - 1;
            Rebuild();
        }

        public void Update(double phase, bool southern, bool up)
        {
            _lit.Fill = up ? _litUp : _litDown;
            _rim.Stroke = up ? _rimUp : _rimDown;

            if (Math.Abs(phase - _phase) < 0.0005 && southern == _southern)
            {
                return;
            }

            _phase = phase;
            _southern = southern;
            Rebuild();
        }

        private void Rebuild()
        {
            var radius = Math.Max(1, (_size - 1) / 2);
            var centre = _size / 2;
            var points = new PointCollection();

            foreach (var point in MoonDisc.LitLimb(centre, centre, radius, _phase, _southern))
            {
                points.Add(new Point(point.X, point.Y));
            }

            _lit.Points = points;
        }
    }

    /// <summary>One label in the number strip: a 16px glyph, a time, and the countdown under it.</summary>
    private sealed class StripLabel
    {
        private readonly FontIcon _icon;
        private readonly TextBlock _value = Fluent.Text(FluentType.BodyStrong);
        private readonly TextBlock _countdown = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");

        public StripLabel(string glyph)
        {
            _icon = Fluent.Icon(glyph);

            _countdown.HorizontalAlignment = HorizontalAlignment.Center;
            _countdown.Visibility = Visibility.Collapsed;

            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _icon, _value },
            };

            Root = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { line, _countdown },
            };
        }

        public StackPanel Root { get; }

        public bool AllowCountdown { get; set; } = true;

        public void ShowIcon(bool visible) => _icon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>The strip's readout is the one thing here that scales, and it scales by step.</summary>
        public void SetStep(TypeStep step) => _value.Apply(step);

        public void Set(string value, string? countdown)
        {
            _value.Text = value;
            _countdown.Text = countdown ?? string.Empty;
            _countdown.Visibility = countdown is not null && AllowCountdown ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// One line of the readout, in the Windows 11 list-row shape: a 16px glyph, what it is, the time
    /// remaining while it is next, and the value. The next event's row is emphasised in the accent
    /// text colour rather than banner-ed.
    /// </summary>
    private sealed class EventRow
    {
        private readonly FrameworkElement _glyph;
        private readonly TextBlock _label = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        private readonly TextBlock _countdown = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly TextBlock _value = Fluent.Text(FluentType.BodyStrong);

        private readonly Brush _primary = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        private readonly Brush _secondary = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        // The accent tuned for text. AccentFillColorDefaultBrush is a fill and is not contrast-safe
        // under small type; this is the key Windows uses when the accent has to be read.
        private readonly Brush _accentText = FluentLayout.Themed("AccentTextFillColorPrimaryBrush", Colors.DodgerBlue);

        public EventRow(FrameworkElement glyph, string label)
        {
            // A Fluent.Icon when it is a glyph; the moon draws its own disc at the same size.
            _glyph = glyph;
            _glyph.VerticalAlignment = VerticalAlignment.Center;
            _glyph.Margin = new Thickness(0, 0, Fluent.Space3, 0);

            _label.Text = label;
            _label.VerticalAlignment = VerticalAlignment.Center;

            _countdown.VerticalAlignment = VerticalAlignment.Center;
            _countdown.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
            _countdown.TextTrimming = TextTrimming.Clip;
            _countdown.Visibility = Visibility.Collapsed;

            _value.HorizontalAlignment = HorizontalAlignment.Right;
            _value.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            Grid.SetColumn(_glyph, 0);
            Grid.SetColumn(_label, 1);
            Grid.SetColumn(_countdown, 2);
            Grid.SetColumn(_value, 3);

            grid.Children.Add(_glyph);
            grid.Children.Add(_label);
            grid.Children.Add(_countdown);
            grid.Children.Add(_value);

            Root = Fluent.Row(grid);
        }

        public Border Root { get; }

        public bool AllowCountdown { get; set; } = true;

        public void SetDensity(bool compact) => Fluent.SetDensity(Root, compact);

        public void SetLabel(string label) => _label.Text = label;

        public void Set(string value, string? countdown)
        {
            _value.Text = value;

            var isNext = countdown is not null;
            _countdown.Text = countdown ?? string.Empty;
            _countdown.Visibility = isNext && AllowCountdown ? Visibility.Visible : Visibility.Collapsed;

            _label.Apply(isNext ? FluentType.BodyStrong : FluentType.Body);
            _label.Foreground = isNext ? _accentText : _secondary;
            _value.Foreground = isNext ? _accentText : _primary;
        }
    }
    /// <summary>
    /// Tell the card what this widget is about. The catalogue can only say "Weather" or
    /// "Folder"; the widget knows which place, which folder, which ticker — and on a desktop
    /// holding three of them, that is the only thing telling them apart.
    /// </summary>
    private void PublishTitle()
    {
        var title = _config.Place;
        _surface?.SetHeaderTitle(string.IsNullOrWhiteSpace(title) ? null : title);
    }

}
