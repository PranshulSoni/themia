using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>The Clock's settings rows. Each writes straight to the live config and saves.</summary>
internal static partial class WidgetSettings
{
    public static void Clock(List<FrameworkElement> rows, ClockConfig clock, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Clock"));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Face", "Digital readout or a sweeping analog dial",
            FluentLayout.Choice(
                [(ClockFace.Digital, "Digital"), (ClockFace.Analog, "Analog")],
                clock.Face,
                value => { clock.Face = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Hours, "24-hour time", null,
            FluentLayout.Toggle(clock.Use24Hour, value => { clock.Use24Hour = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Show seconds",
            "Wakes the app once a second instead of once a minute",
            FluentLayout.Toggle(clock.ShowSeconds, value => { clock.ShowSeconds = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Show date", null,
            FluentLayout.Toggle(clock.ShowDate, value => { clock.ShowDate = value; save(); })));
    }
}
