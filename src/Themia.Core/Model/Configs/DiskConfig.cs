using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>[S-1163] Whose reads and writes the disk chart plots.</summary>
public enum DiskIoScope
{
    /// <summary>The drive the reading is about (the first chosen).</summary>
    Reading = 0,

    /// <summary>Every drive together.</summary>
    All = 1,

    /// <summary>One named drive (<see cref="DiskConfig.IoDrive"/>).</summary>
    Drive = 2,
}

/// <summary>
/// [S-1160] Space used on one or more drives, and how busy they are. Each metric is a toggle saying
/// whether it <i>may</i> appear; the size of the tile decides whether it fits.
/// </summary>
public sealed class DiskConfig : GaugeConfig
{
    /// <summary>
    /// Every five seconds over three minutes, the original's defaults. The history is on, as it was
    /// there: it charts reads and writes, which move, not the space used, which barely does.
    /// </summary>
    public DiskConfig()
        : base(intervalSeconds: 5, graphSpanSeconds: 180)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Disk;

    /// <summary>
    /// [S-1160] The drives to report on, as roots (<c>C:\</c>), in the order chosen. The first is the
    /// reading; several make a drives list. Empty means the system drive.
    /// </summary>
    public List<string> Drives { get; set; } = [];

    /// <summary>
    /// [S-1163] Whose reads and writes the chart plots: the reading's drive, every drive together, or
    /// one named drive (<see cref="IoDrive"/>).
    /// </summary>
    public DiskIoScope IoScope { get; set; } = DiskIoScope.Reading;

    /// <summary>[S-1163] The drive charted when <see cref="IoScope"/> is <see cref="DiskIoScope.Drive"/>, as a root.</summary>
    public string IoDrive { get; set; } = string.Empty;

    /// <summary>[S-1164] Reads and writes per second, across every disk (the original's IOPS).</summary>
    public bool ShowIops { get; set; } = true;

    /// <summary>[S-1165] The volume line: label, file system, fixed or removable.</summary>
    public bool ShowVolume { get; set; } = true;

    /// <summary>[S-1166] List every drive on the machine where the tile has room, not only the chosen ones.</summary>
    public bool ShowAllDrives { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new DiskConfig
        {
            Drives = [.. Drives],
            IoScope = IoScope,
            IoDrive = IoDrive,
            ShowIops = ShowIops,
            ShowVolume = ShowVolume,
            ShowAllDrives = ShowAllDrives,
        };
        CopyTo(copy);
        return copy;
    }

    /// <summary>[S-1160] Keep only drive letters, each once, as roots; a corrupt entry is dropped loudly.</summary>
    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>(base.Repair());

        Drives ??= [];
        var clean = new List<string>(Drives.Count);

        foreach (var drive in Drives)
        {
            if (DiskRules.Root(drive) is not { } root)
            {
                notes.Add($"{Kind}: '{drive}' is not a drive letter; dropped");
                continue;
            }

            if (clean.Contains(root, StringComparer.Ordinal))
            {
                continue;
            }

            if (clean.Count == DiskRules.MaxDrives)
            {
                notes.Add($"{Kind}: more than {DiskRules.MaxDrives} drives named; the rest dropped");
                break;
            }

            clean.Add(root);
        }

        if (!clean.SequenceEqual(Drives, StringComparer.Ordinal))
        {
            Drives = clean;
        }

        if (!Enum.IsDefined(IoScope))
        {
            notes.Add($"{Kind}: I/O scope {(int)IoScope} is unknown; charting the reading's drive");
            IoScope = DiskIoScope.Reading;
        }

        IoDrive ??= string.Empty;

        // A named drive that is not a drive letter charts nothing true: fall back to the reading's.
        if (IoScope == DiskIoScope.Drive)
        {
            if (DiskRules.Root(IoDrive) is { } io)
            {
                IoDrive = io;
            }
            else
            {
                notes.Add($"{Kind}: I/O drive '{IoDrive}' is not a drive letter; charting the reading's drive");
                IoScope = DiskIoScope.Reading;
                IoDrive = string.Empty;
            }
        }

        return notes;
    }
}
