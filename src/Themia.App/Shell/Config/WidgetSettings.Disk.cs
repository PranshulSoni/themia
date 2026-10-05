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
    /// [S-1160] [S-1168] The drive to report on and whose reads and writes the chart plots, then the
    /// rows every meter has ([S-1762]).
    /// </summary>
    public static void Disk(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Disk_Disk.Text()));
        AddDriveRow(rows, disk, save);
        AddIoRow(rows, disk, save);

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
            Str.Shell_Meters_ChartDesc.Text(),
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
    /// [S-1160] The drive the number is about: the system drive (said by name, so the widget's
    /// behaviour is never something to infer), or one of the drives Windows lists. A chosen drive
    /// that has since been removed stays offered, so the choice still reads true.
    /// </summary>
    private static void AddDriveRow(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        var system = DiskRules.Root(DiskMetrics.SystemRoot) ?? "C:\\";
        var options = new List<(string Value, string Label)>
        {
            (string.Empty, Str.Shell_Meters_SystemDrive.Format(("drive", DiskRules.Instance(system)))),
        };

        foreach (var volume in DiskMetrics.Volumes(refresh: true))
        {
            options.Add((volume.Root, DiskRules.Title(volume)));
        }

        var current = DiskRules.Root(disk.Drive) ?? string.Empty;

        if (!options.Any(o => string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add((current, Str.WidgetsB_Settings_Disk_NotConnectedDrive.Format(("drive", DiskRules.Instance(current)))));
        }

        rows.Add(FluentLayout.Row(Glyphs.Disk, Str.Shell_Meters_Drive_Label.Text(), Str.Shell_Meters_Drive_Desc.Text(),
            FluentLayout.Choice(
                [.. options.Select((o, i) => (i, o.Label))],
                Math.Max(0, options.FindIndex(o => string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase))),
                index =>
                {
                    disk.Drive = options[index].Value;
                    save();
                }),
            "drive", "volume", "disk"));
    }
}
