using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Sun &amp; Moon rows: where the observer is, and how much of the sky to draw.
/// <para>
/// [S-1494] The place is picked by city name, as in the original [S-01], through the same search the
/// weather widgets use (<see cref="PlaceSearch"/>). It resolves to coordinates once, at the pick; the
/// astronomy itself stays offline. The exact coordinates stay editable below it, validated, for a
/// place no geocoder knows (a cabin, a ship, a pole).
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void SunMoon(List<FrameworkElement> rows, SunMoonConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Location"));

        // Built before the search so a pick can show its coordinates in them at once.
        var place = new TextBox { Text = config.Place, MinWidth = 200, PlaceholderText = "Zurich" };
        var latitude = new TextBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Right };
        var longitude = new TextBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Right };

        // A Sun & moon tile always has a place (it starts at Zurich), so "Change" only reopens the
        // search; it never clears the coordinates, unlike the weather's, which can have none.
        PlaceSearch.Add(rows, new PlaceSearchBinding(
            string.Empty,
            config.Place,
            _ => { /* ignore: the typed query is not kept; the chosen place is what is stored */ },
            picked =>
            {
                config.Latitude = picked.Latitude;
                config.Longitude = picked.Longitude;
                config.Place = picked.Name;
                place.Text = picked.Name;
                latitude.Text = FormatCoordinate(picked.Latitude);
                longitude.Text = FormatCoordinate(picked.Longitude);
                save();
            },
            () => { }));

        rows.Add(FluentLayout.Header("Exact coordinates"));

        // On lost focus, not per keystroke: the widget behind this window redraws on every write,
        // and a half-typed place name is not a place name.
        place.LostFocus += (_, _) =>
        {
            var text = place.Text.Trim();

            if (text == config.Place)
            {
                return;
            }

            config.Place = text;
            save();
        };

        rows.Add(FluentLayout.Row(Glyphs.Place, "Name", "The name in the header", place));

        rows.Add(CoordinateRow(
            latitude,
            "Latitude",
            "Degrees north, -90 to 90",
            config.Latitude,
            -90,
            90,
            value => { config.Latitude = value; save(); }));

        rows.Add(CoordinateRow(
            longitude,
            "Longitude",
            "Degrees east, -180 to 180",
            config.Longitude,
            -180,
            180,
            value => { config.Longitude = value; save(); }));

        rows.Add(FluentLayout.Header("Sky"));

        rows.Add(FluentLayout.Row(Glyphs.Sun, "Sun path", "The curve of the sun's real path across the sky",
            FluentLayout.Choice(
                [
                    (SunPathMode.Numbers, "Curve and times"),
                    (SunPathMode.Plain, "Curve only"),
                    (SunPathMode.Hidden, "Hidden"),
                ],
                config.PathMode,
                value => { config.PathMode = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.View, "Event rows",
            "Sunrise, sunset, daylight, the moon, then noon and twilight as the tile grows. Always shown while the sun path is hidden",
            FluentLayout.Toggle(config.ShowEvents, value => { config.ShowEvents = value; save(); })));
    }

    /// <summary>
    /// A coordinate entry that rejects what it cannot read.
    /// <para>
    /// Deliberately not a <c>NumberBox</c> and not <c>double.TryParse</c> with a fallback: coercing
    /// unreadable input to 0 would quietly move the user to the Gulf of Guinea and then report
    /// sunrise there as though nothing had happened. Bad input keeps the last good value, says so
    /// on the same line under the box every other refused field uses, and writes nothing.
    /// </para>
    /// </summary>
    private static FrameworkElement CoordinateRow(
        TextBox box,
        string title,
        string description,
        double current,
        double minimum,
        double maximum,
        Action<double> onChanged)
    {
        box.Text = FormatCoordinate(current);

        var verdict = FluentLayout.Verdict();

        void Commit()
        {
            var text = box.Text.Trim();

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value)
                && value >= minimum
                && value <= maximum)
            {
                FluentLayout.Confirm(verdict, string.Empty);
                box.Text = FormatCoordinate(value);
                onChanged(value);
                return;
            }

            // Refused: keep the setting where it was, and leave what was typed on screen next to the
            // reason, so the user can correct it rather than hunt for what changed.
            FluentLayout.Refuse(
                verdict,
                $"Enter a number between {FormatCoordinate(minimum)} and {FormatCoordinate(maximum)}");
        }

        box.LostFocus += (_, _) => Commit();

        box.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
                args.Handled = true;
            }
        };

        return FluentLayout.FieldRow(Glyphs.Grid, title, description, box, verdict);
    }

    private static string FormatCoordinate(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
