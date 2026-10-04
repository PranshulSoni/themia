#if DEBUG
using Themia.App.Services.Web;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-731] A fixed reading and week for the gallery, seeded into the weather caches as fresh answers,
/// so the populated weather layouts render offline and identically on every run (the original's
/// demo mode). Made-up numbers on the real coordinates of Zurich. Debug builds only.
/// </summary>
internal static class WeatherFixture
{
    private const double Latitude = 47.37;
    private const double Longitude = 8.54;

    public static WeatherNowConfig NowConfig()
    {
        Install();
        return new WeatherNowConfig { Place = "Zurich", LocationName = "Zürich, Zurich, Switzerland", Latitude = Latitude, Longitude = Longitude };
    }

    public static WeatherForecastConfig ForecastConfig()
    {
        Install();
        // THEMIA_WEATHER_LAYOUT=rows|columns|grid draws the populated page in that layout while
        // working on it; unset, the tile picks (Auto).
        var layout = Enum.TryParse<ForecastLayout>(Environment.GetEnvironmentVariable("THEMIA_WEATHER_LAYOUT"), ignoreCase: true, out var chosen)
            ? chosen
            : ForecastLayout.Auto;

        return new WeatherForecastConfig { Place = "Zurich", LocationName = "Zürich, Zurich, Switzerland", Latitude = Latitude, Longitude = Longitude, Layout = layout };
    }

    private static void Install()
    {
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        (int Code, double Low, double High, double Rain, double Chance)[] week =
        [
            (2, 11, 19, 0, 10),
            (61, 10, 15, 3.4, 80),
            (80, 9, 14, 1.2, 60),
            (3, 8, 16, 0, 20),
            (0, 7, 18, 0, 0),
            (1, 9, 21, 0, 5),
            (95, 12, 22, 8, 70),
            (2, 11, 19, 0, 15),
            (63, 10, 15, 6.5, 90),
            (3, 9, 14, 0.4, 40),
            (71, -1, 4, 2.1, 70),
            (0, -3, 6, 0, 0),
            (45, 2, 8, 0, 10),
            (1, 4, 12, 0, 5),
        ];

        var days = week.Select((d, i) => new ForecastDay(today.AddDays(i), d.Code, d.Low, d.High, d.Rain)
        {
            PrecipitationChance = d.Chance,
            WindMax = 12 + (i * 3 % 17),
            UvMax = 2 + (i % 4),
            Sunrise = new TimeOnly(7, 31).AddMinutes(i),
            Sunset = new TimeOnly(18, 54).AddMinutes(-2 * i),
        }).ToList();

        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var hours = Enumerable.Range(0, 24).Select(i =>
        {
            var at = hour.AddHours(i);

            // A day's curve: coolest before dawn, warmest mid-afternoon; a shower in the evening.
            var temperature = 15 + (4 * Math.Sin((at.Hour - 9) / 24.0 * 2 * Math.PI));
            double chance = at.Hour is >= 18 and <= 21 ? 55 : at.Hour is >= 15 and < 18 ? 20 : 0;
            return new HourlyPoint(at, Math.Round(temperature, 1), chance);
        }).ToList();

        var reading = new CurrentWeather(18.4, 17.2, 63, 11, 2)
        {
            Precipitation = 0,
            Pressure = 1016,
            WindDirection = 315,
            WindGusts = 24,
            CloudCover = 40,
            UvIndex = 3,
            Days = days.Take(6).ToList(),
            Hours = hours,
        };

        WeatherService.SeedGallery(Latitude, Longitude, reading, new Forecast(days));
    }
}
#endif
