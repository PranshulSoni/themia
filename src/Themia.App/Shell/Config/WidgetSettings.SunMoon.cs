using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.SunMoon_LocationTab.Text()));

        // Built before the search so a pick can show its coordinates in them at once.
        var place = new TextBox { Text = config.Place, MinWidth = 200, PlaceholderText = Str.SunMoon_Place_Placeholder.Text() };
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

        rows.Add(FluentLayout.Header(Str.WidgetsC_SunMoon_Settings_ExactCoordinates.Text()));

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

        rows.Add(FluentLayout.Row(Glyphs.Place, Str.SunMoon_Place_Label.Text(), Str.SunMoon_Place_Desc.Text(), place));

        rows.Add(CoordinateRow(
            latitude,
            Str.SunMoon_Latitude_Label.Text(),
            Str.SunMoon_Latitude_Desc.Text(),
            config.Latitude,
            -90,
            90,
            value => { config.Latitude = value; save(); }));

        rows.Add(CoordinateRow(
            longitude,
            Str.SunMoon_Longitude_Label.Text(),
            Str.SunMoon_Longitude_Desc.Text(),
            config.Longitude,
            -180,
            180,
            value => { config.Longitude = value; save(); }));

        rows.Add(FluentLayout.Header(Str.WidgetsC_SunMoon_Settings_Sky.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Sun, Str.SunMoon_SunPath_Label.Text(), Str.SunMoon_SunPath_Desc.Text(),
            FluentLayout.Choice(
                [
                    (SunPathMode.Numbers, Str.SunMoon_PathMode_Numbers.Text()),
                    (SunPathMode.Plain, Str.SunMoon_PathMode_Plain.Text()),
                    (SunPathMode.Hidden, Str.SunMoon_PathMode_Hidden.Text()),
                ],
                config.PathMode,
                value => { config.PathMode = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.View, Str.SunMoon_Events_Label.Text(),
            Str.WidgetsC_SunMoon_Settings_Events_Desc.Text(),
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
                Str.WidgetsC_SunMoon_Settings_CoordinateRange.Format(("min", FormatCoordinate(minimum)), ("max", FormatCoordinate(maximum))));
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
