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
        AddWeatherDisplay(rows, config, save);

        // [S-724] Auto follows the tile: columns on a strip, rows otherwise.
        rows.Add(FluentLayout.Row(Glyphs.ListLayout, "Layout", "How the days are arranged",
            FluentLayout.Choice(
                [
                    (ForecastLayout.Auto, "Automatic"),
                    (ForecastLayout.Rows, "Rows"),
                    (ForecastLayout.Columns, "Columns"),
                    (ForecastLayout.Grid, "Grid"),
                ],
                config.Layout,
                value => { config.Layout = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Droplet, "Show precipitation",
            "The day's total rain or snow, where there is any",
            FluentLayout.Toggle(config.ShowPrecipitation, value => { config.ShowPrecipitation = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Days", "How far ahead to look, up to 14",
            FluentLayout.Slider(config.EffectiveDays, 1, 14, 1, string.Empty,
                value => { config.Days = (int)Math.Round(value); save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }
}
