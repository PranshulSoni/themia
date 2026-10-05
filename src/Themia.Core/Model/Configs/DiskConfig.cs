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

/// <summary>[S-1160] Space used on one drive, with its reads and writes as the history.</summary>
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

    /// <summary>[S-1160] The drive to report on, as a root (<c>C:\</c>). Empty means the system drive.</summary>
    public string Drive { get; set; } = string.Empty;

    /// <summary>
    /// [S-1163] Whose reads and writes the chart plots: the reading's drive, every drive together, or
    /// one named drive (<see cref="IoDrive"/>).
    /// </summary>
    public DiskIoScope IoScope { get; set; } = DiskIoScope.Reading;

    /// <summary>[S-1163] The drive charted when <see cref="IoScope"/> is <see cref="DiskIoScope.Drive"/>, as a root.</summary>
    public string IoDrive { get; set; } = string.Empty;

    public override WidgetConfig Clone()
    {
        var copy = new DiskConfig
        {
            Drive = Drive,
            IoScope = IoScope,
            IoDrive = IoDrive,
        };
        CopyTo(copy);
        return copy;
    }

    /// <summary>[S-1160] Keep the drive as a root; one that is not a drive letter is dropped loudly (the system drive).</summary>
    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>(base.Repair());

        Drive ??= string.Empty;

        if (Drive.Length > 0)
        {
            if (DiskRules.Root(Drive) is { } root)
            {
                Drive = root;
            }
            else
            {
                notes.Add($"{Kind}: '{Drive}' is not a drive letter; reporting on the system drive");
                Drive = string.Empty;
            }
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
