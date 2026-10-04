using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the mixer.
/// <para>
/// Same shape as the rest of <see cref="WidgetSettings"/>: each control writes straight to the live
/// config and saves, because the widget is on the desktop behind the window and watching it change
/// is the point. Which device first, then what the list holds, then what the rows show.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Mixer(List<FrameworkElement> rows, MixerConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Mixer"));

        // [S-1222] Which device's apps; the default follows Windows.
        rows.Add(AudioDeviceRow(config.DeviceId, config.DeviceName, (id, name) =>
        {
            config.DeviceId = id;
            config.DeviceName = name;
            save();
        }));

        // [S-442] The original request: one slider per app, not one per tab.
        rows.Add(FluentLayout.Row(Glyphs.Group, "One row per app",
            "A browser playing in three tabs gets one slider instead of three",
            FluentLayout.Toggle(config.GroupByApp, value => { config.GroupByApp = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Volume, "Master volume", "The device's own level, as the first row",
            FluentLayout.Toggle(config.ShowMaster, value => { config.ShowMaster = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Play, "Only apps playing now",
            "Leave out apps that hold an audio session but are silent; a large mixer lists them dimmed below",
            FluentLayout.Toggle(config.OnlyActive, value => { config.OnlyActive = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Bell, "System sounds", "Windows' own notification sounds",
            FluentLayout.Toggle(config.ShowSystemSounds, value => { config.ShowSystemSounds = value; save(); })));

        rows.Add(FluentLayout.Header("Rows"));

        rows.Add(FluentLayout.Row(Glyphs.Apps, "App icons", "Each app's own icon rather than a generic one",
            FluentLayout.Toggle(config.ShowIcons, value => { config.ShowIcons = value; save(); })));

        // [S-1223] [S-1224]
        rows.Add(FluentLayout.Row(Glyphs.Volume2, "Level meters", "A live meter under each slider, where there is room",
            FluentLayout.Toggle(config.ShowPeaks, value => { config.ShowPeaks = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Microphone, "Microphone", "The default microphone in the extra-large layout's side panel",
            FluentLayout.Toggle(config.ShowMicrophone, value => { config.ShowMicrophone = value; save(); })));

        rows.Add(FluentLayout.Note("The list follows Windows as apps start and stop playing. Click the mixer, then use ↑ and ↓ to pick a row, ← and → or + and − to change it, and M to mute."));
    }
}
