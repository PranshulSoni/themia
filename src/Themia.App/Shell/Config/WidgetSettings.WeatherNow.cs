using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// The current-conditions widget's settings rows, and the place and display rows both weather
/// widgets share.
/// </summary>
internal static partial class WidgetSettings
{
    public static void WeatherNow(List<FrameworkElement> rows, WeatherNowConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Place"));
        AddPlacePicker(rows, config, save);

        rows.Add(FluentLayout.Header("Weather"));
        AddWeatherDisplay(rows, config, save);

        rows.Add(FluentLayout.Row(Glyphs.Info, "Show detail",
            "Feels-like, humidity, wind and, on larger tiles, today's range and the next hours",
            FluentLayout.Toggle(config.ShowDetails, value => { config.ShowDetails = value; save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }

    /// <summary>
    /// The place rows, shared by both weather widgets: the reusable <see cref="PlaceSearch"/>, bound
    /// to a weather config.
    /// </summary>
    private static void AddPlacePicker(List<FrameworkElement> rows, WeatherConfig config, Action save)
        => PlaceSearch.Add(rows, new PlaceSearchBinding(
            config.Place,
            config.HasLocation ? config.LocationName : null,
            query => config.Place = query,
            place =>
            {
                config.Latitude = place.Latitude;
                config.Longitude = place.Longitude;
                config.LocationName = place.Name;
                save();
            },
            () =>
            {
                // Null, not zero. Zero is the Gulf of Guinea, and a widget cleared to a real
                // coordinate would go on fetching the weather at sea.
                config.Latitude = null;
                config.Longitude = null;
                config.LocationName = string.Empty;
                save();
            }));

    /// <summary>Units and the location-name switch, shared by both weather widgets.</summary>
    private static void AddWeatherDisplay(List<FrameworkElement> rows, WeatherConfig config, Action save)
    {
        rows.Add(FluentLayout.Row(Glyphs.Weather, "Units", "Celsius or Fahrenheit",
            FluentLayout.Choice(
                [(TemperatureUnit.Celsius, "Celsius"), (TemperatureUnit.Fahrenheit, "Fahrenheit")],
                config.Unit,
                value => { config.Unit = value; save(); })));

        // [S-728] Off: the header reads as the widget's label instead of the city.
        rows.Add(FluentLayout.Row(Glyphs.Place, "Location name", "Show the place in the header",
            FluentLayout.Toggle(!config.HideLocation, value => { config.HideLocation = !value; save(); })));
    }
}
