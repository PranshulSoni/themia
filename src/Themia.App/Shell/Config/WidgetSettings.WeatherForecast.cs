using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>The forecast widget's settings rows.</summary>
internal static partial class WidgetSettings
{
    public static void WeatherForecast(List<FrameworkElement> rows, WeatherForecastConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        // The same place picker the current-conditions widget uses, not a copy of it. The original
        // app carried two line-for-line duplicates of its city search and filed a note about it.
        rows.Add(FluentLayout.Header("Place"));
        AddPlacePicker(rows, config, save);

        rows.Add(FluentLayout.Header("Forecast"));

        rows.Add(FluentLayout.Row(Glyphs.Weather, "Units", "Celsius or Fahrenheit",
            FluentLayout.Choice(
                [(TemperatureUnit.Celsius, "Celsius"), (TemperatureUnit.Fahrenheit, "Fahrenheit")],
                config.Unit,
                value => { config.Unit = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Forecast, "Show precipitation",
            "The day's total rain or snow, where there is any",
            FluentLayout.Toggle(config.ShowPrecipitation, value => { config.ShowPrecipitation = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Days", "How far ahead to look, up to 14",
            FluentLayout.Slider(config.EffectiveDays, 1, 14, 1, string.Empty,
                value => { config.Days = (int)Math.Round(value); save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }
}
