using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The clock. Subscribes to the shared <see cref="Ticker"/> at the cadence its config actually
/// needs, and unsubscribes when it leaves the tree — a widget that is not on screen must not keep
/// the process waking up.
/// </summary>
public sealed partial class ClockView : UserControl, IWidgetView
{
    private Ticker? _ticker;
    private ClockConfig _config = new();
    private IDisposable? _subscription;

    public ClockView()
    {
        InitializeComponent();
        Root.SizeChanged += (_, _) => ApplyTypeScale();
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
        SecondHand.Visibility = _config.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        DateText.Visibility = _config.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        MeridiemText.Visibility = _config.Use24Hour ? Visibility.Collapsed : Visibility.Visible;

        ApplyTypeScale();
        Resubscribe();
    }

    /// <summary>
    /// Size the text to the tile so the clock fills whatever box it was given.
    /// <para>
    /// The time is measured against both axes: against width because a long string like
    /// <c>12:34:56 PM</c> needs far more of it than <c>09:05</c>, and against height because a wide,
    /// short tile would otherwise clip. The date takes its share out of the height first, so adding
    /// it shrinks the time rather than pushing it out of the tile.
    /// </para>
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;

        // Roughly how wide one glyph is relative to the font size, times the number of them: a
        // 5-character clock wants a much larger size than an 11-character one in the same box.
        var characters = (_config.ShowSeconds ? 8 : 5) + (_config.Use24Hour ? 0 : 3);
        var widthFactor = 1.0 / (characters * 0.62);

        var timeShare = _config.ShowDate ? 0.62 : 0.85;

        TimeText.FontSize = TypeScale.Fit(width, height, widthFactor, timeShare, 14, 240);
        TimeText.LineHeight = TimeText.FontSize * 1.05;

        MeridiemText.FontSize = Math.Max(9, TimeText.FontSize * 0.28);
        MeridiemText.Margin = new Thickness(0, 0, 0, TimeText.FontSize * 0.12);

        // Its own clamp rather than a fraction of the time: on a very large tile a proportional date
        // becomes a second headline competing with the clock.
        DateText.FontSize = TypeScale.Fit(width, height, widthFactor * 0.42, 0.14, 10, 34);
    }

    private void Resubscribe()
    {
        _subscription?.Dispose();
        _subscription = _ticker?.Subscribe(_config.TickInterval, Render);
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

        HourRotation.Angle = reading.HourAngle;
        MinuteRotation.Angle = reading.MinuteAngle;
        SecondRotation.Angle = reading.SecondAngle;
    }

    public void Detach()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
