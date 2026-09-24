using Microsoft.UI;
using Microsoft.UI.Text;
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
/// inside the polar circles. [S-11] Elapsed daylight is filled in behind the sun; the night stretch
/// carries on the same way round the sky, sets right, nadir, rises left, with its depth compressed
/// against the daylight scale because a true-to-scale winter midnight would be deeper than the
/// whole widget. [S-12]
/// </para>
/// <para>
/// The moon sits <i>beside</i> the graphic, never on the solar arc: the two do not share a track,
/// and drawing it there would imply they do. [S-13]
/// </para>
/// <para>
/// All the astronomy lives in <see cref="Themia.Core.Astronomy"/> as pure functions of an instant,
/// so this file only turns numbers into shapes.
/// </para>
/// </summary>
internal sealed partial class SunMoonView : Grid, IWidgetView
{
    /// <summary>[S-14] The graphic is never dropped; below this it is simply drawn smaller.</summary>
    private const double GraphicFloor = 46;

    private readonly TextBlock _place = new();
    private readonly FontIcon _placeIcon = new();
    private readonly StackPanel _header;

    private readonly Canvas _canvas = new();
    private readonly Polygon _elapsedFill = new();
    private readonly Polyline _dayTrack = new();
    private readonly Polyline _nightTrack = new();
    private readonly Polyline _doneTrack = new();
    private readonly Line _horizon = new();
    private readonly Ellipse _sunGlow = new();
    private readonly Ellipse _sun = new();

    private readonly Grid _strip;
    private readonly StripLabel _riseLabel;
    private readonly StripLabel _dayLabel;
    private readonly StripLabel _setLabel;

    private readonly MoonVisual _moonBadge;
    private readonly TextBlock _moonPercent = new();
    private readonly StackPanel _moonPanel;

    private readonly Grid _body;
    private readonly StackPanel _rowList = new();
    private readonly EventRow _sunriseRow;
    private readonly EventRow _sunsetRow;
    private readonly EventRow _daylightRow;
    private readonly EventRow _moonRow;
    private readonly MoonVisual _moonGlyph;

    private readonly Brush _primary;
    private readonly Brush _muted;
    private readonly Brush _subtle;
    private readonly Brush _accent;
    private readonly Brush _sunBrush;

    private SunMoonConfig _config = new();
    private Ticker? _ticker;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private DateTimeOffset _now = DateTimeOffset.Now;

    public SunMoonView()
    {
        _primary = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        _muted = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gainsboro);
        _subtle = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        _accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        _sunBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);

        _header = BuildHeader();
        BuildCanvas();

        _riseLabel = new StripLabel(Glyphs.Sun, _muted, _subtle);
        _dayLabel = new StripLabel(Glyphs.Clock, _muted, _subtle);
        _setLabel = new StripLabel(Glyphs.Sun, _muted, _subtle);
        _strip = BuildStrip();

        _moonBadge = new MoonVisual(_primary, _subtle);
        _moonGlyph = new MoonVisual(_primary, _subtle);
        _moonPanel = BuildMoonPanel();

        // The next event's row is emphasised in the accent colour, never a colour of its own. [S-15]
        _sunriseRow = new EventRow(new FontIcon { Glyph = Glyphs.Sun }, "Sunrise", _muted, _subtle, _accent);
        _sunsetRow = new EventRow(new FontIcon { Glyph = Glyphs.Sun }, "Sunset", _muted, _subtle, _accent);
        _daylightRow = new EventRow(new FontIcon { Glyph = Glyphs.Clock }, "Daylight", _muted, _subtle, _accent);
        _moonRow = new EventRow(_moonGlyph.Root, "Moon", _muted, _subtle, _accent);

        _rowList.Spacing = 2;
        _rowList.Children.Add(_sunriseRow.Root);
        _rowList.Children.Add(_sunsetRow.Root);
        _rowList.Children.Add(_daylightRow.Root);
        _rowList.Children.Add(_moonRow.Root);

        // The graphic and its number strip share a column; the moon takes the one beside it, so it
        // can never be drawn on top of the sun's arc. [S-13]
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

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        SetRow(_header, 0);
        SetRow(_body, 1);
        SetRow(_rowList, 2);

        Children.Add(_header);
        Children.Add(_body);
        Children.Add(_rowList);

        SizeChanged += (_, _) => ApplyLayout();
        _canvas.SizeChanged += (_, _) => Redraw();
    }

    private StackPanel BuildHeader()
    {
        _placeIcon.Glyph = Glyphs.Place;
        _placeIcon.Foreground = _muted;
        _placeIcon.VerticalAlignment = VerticalAlignment.Center;

        _place.Foreground = _muted;
        _place.FontWeight = FontWeights.SemiBold;
        _place.TextTrimming = TextTrimming.CharacterEllipsis;
        _place.VerticalAlignment = VerticalAlignment.Center;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { _placeIcon, _place },
        };
    }

    private void BuildCanvas()
    {
        _elapsedFill.Fill = _sunBrush;
        _elapsedFill.Opacity = 0.20;

        _dayTrack.Stroke = _subtle;
        _dayTrack.StrokeThickness = 1.25;
        _dayTrack.StrokeLineJoin = PenLineJoin.Round;

        _nightTrack.Stroke = _subtle;
        _nightTrack.StrokeThickness = 1;
        _nightTrack.Opacity = 0.7;
        _nightTrack.StrokeDashArray = [3, 3];

        _doneTrack.Stroke = _sunBrush;
        _doneTrack.StrokeThickness = 1.75;
        _doneTrack.StrokeLineJoin = PenLineJoin.Round;

        _horizon.Stroke = _subtle;
        _horizon.StrokeThickness = 1;
        _horizon.Opacity = 0.5;

        _sunGlow.Fill = _sunBrush;
        _sunGlow.Opacity = 0.18;

        _sun.Fill = _sunBrush;

        _canvas.MinHeight = GraphicFloor;
        _canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
        _canvas.VerticalAlignment = VerticalAlignment.Stretch;
        _canvas.IsHitTestVisible = false;

        _canvas.Children.Add(_elapsedFill);
        _canvas.Children.Add(_dayTrack);
        _canvas.Children.Add(_nightTrack);
        _canvas.Children.Add(_doneTrack);
        _canvas.Children.Add(_horizon);
        _canvas.Children.Add(_sunGlow);
        _canvas.Children.Add(_sun);
    }

    /// <summary>
    /// The number strip. Its three columns put each label on the gridline of the curve point it
    /// names — 13%, 50% and 87%, the same anchors the plot uses for sunrise, noon and sunset.
    /// [S-16]
    /// </summary>
    private Grid BuildStrip()
    {
        var strip = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0),
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
        _moonPercent.Foreground = _subtle;
        _moonPercent.HorizontalAlignment = HorizontalAlignment.Center;

        return new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(6, 2, 0, 0),
            Children = { _moonBadge.Root, _moonPercent },
        };
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as SunMoonConfig ?? new SunMoonConfig();

        _place.Text = string.IsNullOrWhiteSpace(_config.Place) ? "Sun & Moon" : _config.Place.Trim();

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
    /// What is shown at this size. [S-10] [S-14]
    /// <para>
    /// The graphic is never dropped — the user asked for it — so only extras shed as the tile
    /// shrinks: first the countdown, then the daylight label, then the label icons and the moon's
    /// percentage, then the number strip; and among the rows the moon, then daylight, then the two
    /// events, but only while the graphic is there to carry them. With the list on its own those two
    /// always survive.
    /// </para>
    /// </summary>
    private void ApplyLayout()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        var graphic = _config.PathMode != SunPathMode.Hidden;

        // The two settings are independent, with one guard: with the graphic hidden the rows are
        // forced on, since a widget showing nothing is never the answer. [S-10]
        var rows = _config.ShowEvents || !graphic;
        var numbers = graphic && _config.PathMode == SunPathMode.Numbers;

        // The thresholds are the height the shed-able parts need at their floor font size, plus the
        // graphic's own floor, so nothing ever has to be clipped to fit.
        var showStrip = numbers && height >= 80 && width >= 120;
        var showCountdown = width >= 190 && height >= 120;
        var showDaylightLabel = width >= 140;
        var showSmallExtras = width >= 160;

        var showEventRows = rows && (!graphic || height >= 130);
        var showDaylightRow = rows && (!graphic || height >= 165);
        var showMoonRow = rows && (!graphic || height >= 195);

        _header.Visibility = Show(height >= 110 && width >= 100);
        _canvas.Visibility = Show(graphic);
        _moonPanel.Visibility = Show(graphic);
        _body.Visibility = Show(graphic);

        // Under a graphic the rows sit at the bottom; on their own they sit at the top, because
        // nothing above them is pushing them down. [S-16]
        RowDefinitions[1].Height = graphic ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        RowDefinitions[2].Height = graphic ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _rowList.VerticalAlignment = graphic ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _strip.Visibility = Show(showStrip);
        _rowList.Visibility = Show(showEventRows || showDaylightRow || showMoonRow);

        _sunriseRow.Root.Visibility = Show(showEventRows);
        _sunsetRow.Root.Visibility = Show(showEventRows);
        _daylightRow.Root.Visibility = Show(showDaylightRow);
        _moonRow.Root.Visibility = Show(showMoonRow);

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

        ApplyTypeScale(width, height);
        Refresh();
    }

    private void ApplyTypeScale(double width, double height)
    {
        var header = TypeScale.Fit(width, height, 0.10, 0.09, 10, 18);
        _place.FontSize = header;
        _placeIcon.FontSize = header;

        var label = TypeScale.Fit(width, height, 0.075, 0.070, 9, 20);
        _riseLabel.SetFontSize(label);
        _dayLabel.SetFontSize(label);
        _setLabel.SetFontSize(label);
        _moonPercent.FontSize = Math.Max(9, label - 1);

        var row = TypeScale.Fit(width, height, 0.075, 0.075, 10, 22);
        _sunriseRow.SetFontSize(row);
        _sunsetRow.SetFontSize(row);
        _daylightRow.SetFontSize(row);
        _moonRow.SetFontSize(row);

        _moonGlyph.Resize(Math.Clamp(row + 3, 12, 28));
        _moonBadge.Resize(Math.Clamp(Math.Min(width * 0.16, height * 0.26), 14, 48));
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
        // emphasised. There is no separate banner. [S-15]
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
    /// Rebuild the drawing. Cheap by construction: seven shapes and about a hundred points,
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
        var radius = Math.Clamp(Math.Min(width, height) * 0.055, 3, 14);

        Place(_sun, marker, radius);
        Place(_sunGlow, marker, radius * 2.4);

        // The sun below the horizon is still drawn, dimmed, so the night stretch reads as the same
        // journey rather than as an empty track.
        _sun.Opacity = position.Up ? 1 : 0.45;
        _sunGlow.Visibility = Show(position.Up);

        // A glow at the edge of the plot would otherwise bleed over the rows beneath it.
        _canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
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
    /// is an em dash, because inside the polar circles there is no event to show. [S-03]
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
    /// The moon at its current phase. [S-13]
    /// <para>
    /// The lit limb comes from <see cref="MoonDisc"/>, so the badge beside the graphic and the glyph
    /// in the row list are the same drawing and cannot disagree about how much of the moon is lit.
    /// The unlit part keeps a hairline rim, so a thin crescent still reads as a moon rather than as
    /// a stray mark, and the whole thing dims below the horizon.
    /// </para>
    /// </summary>
    private sealed class MoonVisual
    {
        private readonly Ellipse _rim = new();
        private readonly Polygon _lit = new();

        private double _size;
        private double _phase;
        private bool _southern;

        public MoonVisual(Brush lit, Brush rim)
        {
            _rim.Stroke = rim;
            _rim.StrokeThickness = 1;
            _rim.Fill = null;

            _lit.Fill = lit;

            Root = new Grid { Children = { _rim, _lit } };
            Resize(16);
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
            Root.Opacity = up ? 1 : 0.5;

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

    /// <summary>One label in the number strip: a glyph, a time, and the countdown under it. [S-16]</summary>
    private sealed class StripLabel
    {
        private readonly FontIcon _icon = new();
        private readonly TextBlock _value = new();
        private readonly TextBlock _countdown = new();
        private readonly StackPanel _line;

        public StripLabel(string glyph, Brush value, Brush subtle)
        {
            _icon.Glyph = glyph;
            _icon.Foreground = subtle;
            _icon.VerticalAlignment = VerticalAlignment.Center;

            _value.Foreground = value;
            _value.FontWeight = FontWeights.SemiBold;

            _countdown.Foreground = subtle;
            _countdown.HorizontalAlignment = HorizontalAlignment.Center;
            _countdown.Visibility = Visibility.Collapsed;

            _line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _icon, _value },
            };

            Root = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _line, _countdown },
            };
        }

        public StackPanel Root { get; }

        public bool AllowCountdown { get; set; } = true;

        public void ShowIcon(bool visible) => _icon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        public void SetFontSize(double size)
        {
            _value.FontSize = size;
            _icon.FontSize = Math.Max(9, size - 1);
            _countdown.FontSize = Math.Max(9, size - 2);
        }

        public void Set(string value, string? countdown)
        {
            _value.Text = value;
            _countdown.Text = countdown ?? string.Empty;
            _countdown.Visibility = countdown is not null && AllowCountdown ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// One line of the readout: a glyph, what it is, the time remaining while it is next, and the
    /// value. The next event's row is emphasised rather than banner-ed. [S-15]
    /// </summary>
    private sealed class EventRow
    {
        private readonly FrameworkElement _glyph;
        private readonly TextBlock _label = new();
        private readonly TextBlock _countdown = new();
        private readonly TextBlock _value = new();
        private readonly Brush _muted;
        private readonly Brush _strong;

        public EventRow(FrameworkElement glyph, string label, Brush muted, Brush subtle, Brush strong)
        {
            _glyph = glyph;
            _muted = muted;
            _strong = strong;

            _glyph.VerticalAlignment = VerticalAlignment.Center;
            _glyph.Margin = new Thickness(0, 0, 6, 0);

            if (_glyph is FontIcon icon)
            {
                icon.Foreground = subtle;
            }

            _label.Text = label;
            _label.Foreground = muted;
            _label.VerticalAlignment = VerticalAlignment.Center;

            _countdown.Foreground = subtle;
            _countdown.VerticalAlignment = VerticalAlignment.Center;
            _countdown.Margin = new Thickness(6, 0, 0, 0);
            _countdown.TextTrimming = TextTrimming.Clip;
            _countdown.Visibility = Visibility.Collapsed;

            _value.Foreground = muted;
            _value.HorizontalAlignment = HorizontalAlignment.Right;
            _value.VerticalAlignment = VerticalAlignment.Center;
            _value.FontWeight = FontWeights.SemiBold;

            Root = new Grid
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

            Root.Children.Add(_glyph);
            Root.Children.Add(_label);
            Root.Children.Add(_countdown);
            Root.Children.Add(_value);
        }

        public Grid Root { get; }

        public bool AllowCountdown { get; set; } = true;

        public void SetFontSize(double size)
        {
            _label.FontSize = size;
            _value.FontSize = size;
            _countdown.FontSize = Math.Max(9, size - 2);

            if (_glyph is FontIcon icon)
            {
                icon.FontSize = size;
            }
        }

        public void SetLabel(string label) => _label.Text = label;

        public void Set(string value, string? countdown)
        {
            _value.Text = value;

            var isNext = countdown is not null;
            _countdown.Text = countdown ?? string.Empty;
            _countdown.Visibility = isNext && AllowCountdown ? Visibility.Visible : Visibility.Collapsed;

            _label.Foreground = isNext ? _strong : _muted;
            _value.Foreground = isNext ? _strong : _muted;
            _label.FontWeight = isNext ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
