using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    public static void Disk(List<FrameworkElement> rows, DiskConfig disk, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Disk"));

        var drives = SystemMetrics.FixedDrives();
        var current = SystemMetrics.NormaliseDrive(disk.Drive);

        // A drive that has since been removed would otherwise leave the box blank and the setting
        // silently pointing at nothing.
        if (!drives.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            drives.Insert(0, current);
        }

        var combo = new ComboBox { MinWidth = 150 };
        foreach (var drive in drives)
        {
            combo.Items.Add(new ComboBoxItem { Content = drive, Tag = drive });
        }

        combo.SelectedIndex = Math.Max(0, drives.FindIndex(d => d.Equals(current, StringComparison.OrdinalIgnoreCase)));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string drive })
            {
                disk.Drive = drive;
                save();
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Disk, "Drive", "Which volume to report on", combo));
        MeterRows(rows, disk, save, "free space is read");
    }
}
