using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    /// <summary>
    /// [S-1160] [S-1168] The drives to report on (several may be chosen; the first is the reading),
    /// then the metrics. Each metric says whether a figure may appear; the tile decides if it fits.
    /// </summary>
    public static void Disk(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Disk_Disk.Text()));
        AddDriveRows(rows, disk, save);

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Meters_Show.Text()));

        AddIoRow(rows, disk, save);

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsB_Settings_Disk_Operations_Label.Text(),
            Str.WidgetsB_Settings_Disk_Operations_Desc.Text(),
            FluentLayout.Toggle(disk.ShowIops, value => { disk.ShowIops = value; save(); }),
            "iops"));

        rows.Add(FluentLayout.Row(Glyphs.Info, Str.WidgetsB_Settings_Disk_Volume_Label.Text(),
            Str.WidgetsB_Settings_Disk_Volume_Desc.Text(),
            FluentLayout.Toggle(disk.ShowVolume, value => { disk.ShowVolume = value; save(); }),
            "label", "file system", "ntfs"));

        rows.Add(FluentLayout.Row(Glyphs.ListLayout, Str.WidgetsB_Settings_Disk_AllDrives_Label.Text(),
            Str.WidgetsB_Settings_Disk_AllDrives_Desc.Text(),
            FluentLayout.Toggle(disk.ShowAllDrives, value => { disk.ShowAllDrives = value; save(); }),
            "per disk", "drives list"));

        MeterRows(rows, disk, save, Str.WidgetsB_Settings_Disk_Refresh.Text());
    }

    /// <summary>
    /// [S-1163] Whose reads and writes the chart plots: the reading's drive, every drive together, or
    /// one drive by name. A closed choice, stored as <see cref="DiskIoScope"/> plus the root.
    /// </summary>
    private static void AddIoRow(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        const string Reading = "reading";
        const string All = "all";

        var options = new List<(string Value, string Label)> { (Reading, Str.WidgetsB_Settings_Disk_ChartSame.Text()), (All, Str.WidgetsB_Settings_Disk_ChartAll.Text()) };

        foreach (var volume in DiskMetrics.Volumes(refresh: false))
        {
            options.Add((volume.Root, DiskRules.Title(volume)));
        }

        var current = disk.IoScope switch
        {
            DiskIoScope.All => All,
            DiskIoScope.Drive when DiskRules.Root(disk.IoDrive) is { } root => root,
            _ => Reading,
        };

        // A named drive that is not connected stays offered, so the choice still reads true.
        if (!options.Any(o => o.Value == current))
        {
            options.Add((current, Str.WidgetsB_Settings_Disk_NotConnectedDrive.Format(("drive", DiskRules.Instance(current)))));
        }

        rows.Add(FluentLayout.Row(Glyphs.Motion, Str.WidgetsB_Settings_Disk_Chart_Label.Text(),
            Str.WidgetsB_Settings_Disk_Chart_Desc.Text(),
            FluentLayout.Choice(
                [.. options.Select((o, i) => (i, o.Label))],
                Math.Max(0, options.FindIndex(o => o.Value == current)),
                index =>
                {
                    var value = options[index].Value;
                    (disk.IoScope, disk.IoDrive) = value switch
                    {
                        All => (DiskIoScope.All, string.Empty),
                        Reading => (DiskIoScope.Reading, string.Empty),
                        _ => (DiskIoScope.Drive, value),
                    };
                    save();
                }),
            "read", "write", "throughput", "activity", "i/o"));
    }

    /// <summary>
    /// One switch per drive, in the order Windows lists them; the drives switched on are the ones the
    /// widget reports on, in that order. None switched on means the system drive, said under the
    /// header so the widget's behaviour is never something to infer from a row of switches all off.
    /// </summary>
    private static void AddDriveRows(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        var volumes = DiskMetrics.Volumes(refresh: true).ToList();
        var chosen = disk.Drives.Select(DiskRules.Root).OfType<string>().ToList();

        // A drive that has since been removed stays listed, so it can be switched off rather than
        // left silently pointing at nothing.
        foreach (var root in chosen)
        {
            if (!volumes.Any(v => string.Equals(v.Root, root, StringComparison.OrdinalIgnoreCase)))
            {
                volumes.Add(new DiskVolume(root, Str.WidgetsB_Settings_Disk_NotConnectedLabel.Text(), string.Empty, true, 0, 0));
            }
        }

        var system = DiskRules.Root(DiskMetrics.SystemRoot) ?? "C:\\";
        var switches = new List<Border>(volumes.Count);

        foreach (var volume in volumes)
        {
            var root = volume.Root;
            var on = chosen.Contains(root, StringComparer.OrdinalIgnoreCase);
            var detail = volume.Total > 0 ? DiskRules.FreeOfTotal(volume) : Str.WidgetsB_Settings_Disk_NotConnected.Text();

            switches.Add(FluentLayout.Row(Glyphs.Disk, DiskRules.Title(volume), detail,
                FluentLayout.Toggle(on, value =>
                {
                    // Rebuilt in the order the switches are listed, so the first switched on is the reading.
                    var now = disk.Drives.Select(DiskRules.Root).OfType<string>().ToList();
                    now.RemoveAll(d => string.Equals(d, root, StringComparison.OrdinalIgnoreCase));

                    if (value)
                    {
                        now.Add(root);
                    }

                    disk.Drives = [.. volumes.Select(v => v.Root).Where(r => now.Contains(r, StringComparer.OrdinalIgnoreCase))];
                    save();
                })));
        }

        rows.Add(FluentLayout.Expander(Glyphs.Disk, Str.WidgetsB_Settings_Disk_Drives_Label.Text(),
            Str.WidgetsB_Settings_Disk_Drives_Desc.Format(("drive", DiskRules.Instance(system))),
            null, switches, expanded: true));
    }
}
