using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The next few days, from open-meteo's <c>daily</c> block.
/// <para>
/// A separate widget from <see cref="WeatherNowConfig"/> rather than a mode of it, because the two
/// answer different questions and want different shapes on the desktop: one is a readout, the other
/// is a list.
/// </para>
/// </summary>
public sealed class WeatherForecastConfig : WeatherConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.WeatherForecast;

    /// <summary>How many days to ask for, 1 to 14 — open-meteo's own limit.</summary>
    public int Days { get; set; } = 7;

    /// <summary>Show each day's precipitation total beside its temperatures.</summary>
    public bool ShowPrecipitation { get; set; } = true;

    /// <summary>The day count actually sent, clamped, so a hand-edited 0 or 99 cannot reach the URL.</summary>
    [JsonIgnore]
    public int EffectiveDays => Math.Clamp(Days, 1, 14);

    public WeatherForecastConfig()
    {
        // A forecast for tomorrow does not change every quarter of an hour, and the free endpoint
        // is shared by everyone who uses it.
        RefreshMinutes = 60;
    }

    public override WidgetConfig Clone()
    {
        var copy = new WeatherForecastConfig
        {
            Days = Days,
            ShowPrecipitation = ShowPrecipitation,
        };

        CopyTo(copy);
        return copy;
    }
}
