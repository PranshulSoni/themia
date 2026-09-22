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

        Resubscribe();
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
