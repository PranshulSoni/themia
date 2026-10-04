using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
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

        rows.Add(FluentLayout.Header("Disk"));
        AddDriveRows(rows, disk, save);

        rows.Add(FluentLayout.Header("Show"));

        AddIoRow(rows, disk, save);

        rows.Add(FluentLayout.Row(Glyphs.Count, "Operations",
            "Reads and writes per second, across every disk",
            FluentLayout.Toggle(disk.ShowIops, value => { disk.ShowIops = value; save(); }),
            "iops"));

        rows.Add(FluentLayout.Row(Glyphs.Info, "Volume",
            "The drive's label and file system",
            FluentLayout.Toggle(disk.ShowVolume, value => { disk.ShowVolume = value; save(); }),
            "label", "file system", "ntfs"));

        rows.Add(FluentLayout.Row(Glyphs.ListLayout, "All drives",
            "List every drive on this PC where the tile has room, not only the ones chosen",
            FluentLayout.Toggle(disk.ShowAllDrives, value => { disk.ShowAllDrives = value; save(); }),
            "per disk", "drives list"));

        MeterRows(rows, disk, save, "the drives are read");
    }

    /// <summary>
    /// [S-1163] Whose reads and writes the chart plots: the reading's drive, every drive together, or
    /// one drive by name. A closed choice, stored as <see cref="DiskIoScope"/> plus the root.
    /// </summary>
    private static void AddIoRow(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        const string Reading = "reading";
        const string All = "all";

        var options = new List<(string Value, string Label)> { (Reading, "Same drive as the reading"), (All, "All drives") };

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
            options.Add((current, $"{DiskRules.Instance(current)} (not connected)"));
        }

        rows.Add(FluentLayout.Row(Glyphs.Motion, "Chart",
            "Whose reads and writes the chart shows; space is the number and the bar",
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
                volumes.Add(new DiskVolume(root, "not connected", string.Empty, true, 0, 0));
            }
        }

        var system = DiskRules.Root(DiskMetrics.SystemRoot) ?? "C:\\";
        var switches = new List<Border>(volumes.Count);

        foreach (var volume in volumes)
        {
            var root = volume.Root;
            var on = chosen.Contains(root, StringComparer.OrdinalIgnoreCase);
            var detail = volume.Total > 0 ? DiskRules.FreeOfTotal(volume) : "Not connected";

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

        rows.Add(FluentLayout.Expander(Glyphs.Disk, "Drives",
            $"Which drives to report on; the first is the big number. None means the system drive ({DiskRules.Instance(system)})",
            null, switches, expanded: true));
    }
}
