namespace Themia.App.Services;

/// <summary>One labelled figure: "Free" / "844 GB", "Available" / "5.9 GB".</summary>
public readonly record struct MeterFact(string Label, string Value);

/// <summary>
/// One row of a meter's breakdown list: a top process, a core, a drive, an interface.
/// </summary>
/// <param name="Name">What the row is.</param>
/// <param name="Value">Its figure, already formatted.</param>
/// <param name="Fraction">Its share, 0..1, for a mini bar under the row; null draws none.</param>
/// <param name="Tooltip">More about it on hover (a pid, a full path); null for none.</param>
public readonly record struct MeterRow(string Name, string Value, double? Fraction = null, string? Tooltip = null);

/// <summary>[S-1127] A meter's breakdown list and its heading: "Top processes", "Cores", "Drives".</summary>
public sealed record MeterList(string Title, IReadOnlyList<MeterRow> Rows);

/// <summary>Which colour a composition segment takes.</summary>
public enum MeterTone
{
    /// <summary>The part the meter is about (in use): the meter's own pressure colour.</summary>
    Primary = 0,

    /// <summary>A secondary share (cached): the same colour, quieter.</summary>
    Secondary = 1,

    /// <summary>What is left (free): the empty track.</summary>
    Rest = 2,
}

/// <summary>[S-1128] One part of a composition bar: "In use 9.8 GB", "Cached 4.1 GB", "Free 2.1 GB".</summary>
public readonly record struct MeterSegment(string Label, string Value, double Fraction, MeterTone Tone);

/// <summary>One reading from a meter. <see cref="Fraction"/> is 0..1, or null when unavailable.</summary>
/// <param name="Fraction">The value the bar shows.</param>
/// <param name="Primary">The big number.</param>
/// <param name="Secondary">The line under it, or null.</param>
/// <param name="Available">False when the machine cannot answer (no battery, drive gone).</param>
/// <param name="Absent">
/// True when the thing being measured does not exist on this machine at all, as opposed to
/// existing but being momentarily unreadable. A desktop with no battery is not a battery meter
/// that failed, and the two want different things on screen: one is an empty state, the other
/// is a dimmed <c>--</c>. Carried as a flag because the view used to tell them apart by
/// matching the reason string, which made a display decision depend on the exact wording of a
/// caption in another file.
/// </param>
/// <param name="Value">
/// [S-664] The reading in its own unit — bytes per second, milliseconds — for a meter whose
/// <see cref="Fraction"/> is a position on a scale rather than the value itself. Null means the
/// fraction is the value (a percentage meter). Its history is what a large tile summarises, and
/// what a window-scaled chart plots.
/// </param>
/// <param name="Facts">
/// [S-664] The reading broken down — used, free, total — for a large tile to show as stat tiles in
/// place of min / avg / max, on a meter whose value does not move. Exactly three, or null.
/// </param>
/// <param name="Second">
/// [S-1126] A second series for the chart (upload beside download, writes beside reads), in the
/// same terms the first is plotted in: a fraction on a share chart, a value on a window-scaled one.
/// </param>
/// <param name="Details">
/// [S-1127] Detail lines in priority order ("Available 5.9 GB", "Swap 1.2 of 4 GB · 30%"): the
/// layout shows as many as fit, first first.
/// </param>
/// <param name="List">[S-1127] The breakdown list (top processes, cores, drives), or null.</param>
/// <param name="Segments">[S-1128] A composition bar (in use / cached / free), or null.</param>
public readonly record struct MeterReading(
    double? Fraction,
    string Primary,
    string? Secondary,
    bool Available,
    bool Absent = false,
    double? Value = null,
    IReadOnlyList<MeterFact>? Facts = null,
    double? Second = null,
    IReadOnlyList<MeterFact>? Details = null,
    MeterList? List = null,
    IReadOnlyList<MeterSegment>? Segments = null)
{
    public static MeterReading Unavailable(string reason) => new(null, "--", reason, false);

    /// <summary>The thing being measured is not present on this machine.</summary>
    public static MeterReading Missing(string reason) => new(null, "--", reason, false, Absent: true);
}
