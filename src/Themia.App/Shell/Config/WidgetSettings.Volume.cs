using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the volume tile.
/// <para>
/// Same shape as the rest of <see cref="WidgetSettings"/>: each control writes straight to the live
/// config and saves, because the widget is on the desktop behind the window and watching it change
/// is the point.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Volume(List<FrameworkElement> rows, VolumeConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Volume"));

        rows.Add(FluentLayout.Row(Glyphs.Sound, "Show the slider",
            "With it off the tile is a readout",
            FluentLayout.Toggle(config.ShowSlider, value => { config.ShowSlider = value; save(); })));

        // Named for what it does rather than "read-only": a desktop widget sits under the path of
        // every pointer on the screen, and somebody who wants a gauge and not a control should be
        // able to say so without losing the gauge.
        rows.Add(FluentLayout.Row(Glyphs.Edit, "Allow changes",
            "Let this tile move the system volume",
            FluentLayout.Toggle(config.AllowControl, value => { config.AllowControl = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Every",
            "Seconds between reads of the output device",
            FluentLayout.Slider(config.IntervalSeconds, 1, 30, 1, "s",
                value => { config.IntervalSeconds = (int)Math.Round(value); save(); })));
    }
}
