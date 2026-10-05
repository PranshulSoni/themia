using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>One volume as the disk meter sees it: its root, its label and file system, and how full it is.</summary>
/// <param name="Root">The drive root, <c>C:\</c>.</param>
/// <param name="Label">The volume label ("System"), or empty.</param>
/// <param name="FileSystem">"NTFS", "exFAT", or empty when Windows did not say.</param>
/// <param name="Removable">A removable drive rather than a fixed one.</param>
/// <param name="Total">Size in bytes; 0 when it could not be read.</param>
/// <param name="Free">Free bytes (for every user, as Explorer shows).</param>
public readonly record struct DiskVolume(string Root, string Label, string FileSystem, bool Removable, ulong Total, ulong Free)
{
    public ulong Used => Total > Free ? Total - Free : 0;

    /// <summary>The share in use, 0..1, or null for a volume whose size is unknown.</summary>
    public double? UsedFraction => Total == 0 ? null : (double)Used / Total;
}

/// <summary>Reads and writes per second on one volume, from the performance counters.</summary>
public readonly record struct DiskRates(double Read, double Write);

/// <summary>
/// [S-1160]..[S-1163] The disk meter's rules: how a drive is named and matched to its
/// performance-counter instance, and whose rates the chart plots. Pure, so each is tested.
/// </summary>
public static class DiskRules
{
    /// <summary>[S-1163] Every rate chart's lowest top: 1 KB/s, so an idle disk stays a flat line, as the original scaled it.</summary>
    public const double ChartFloor = 1024;

    /// <summary>
    /// [S-1160] The root a setting names: <c>c</c>, <c>C:</c> and <c>C:\</c> are all <c>C:\</c>.
    /// Null for anything that is not a drive letter — a folder, a UNC path, a hand-edited typo.
    /// </summary>
    public static string? Root(string? drive)
    {
        var text = drive?.Trim() ?? string.Empty;

        if (text.Length is < 1 or > 3 || !char.IsAsciiLetter(text[0]))
        {
            return null;
        }

        if (text.Length >= 2 && text[1] != ':')
        {
            return null;
        }

        if (text.Length == 3 && text[2] != '\\' && text[2] != '/')
        {
            return null;
        }

        return $"{char.ToUpperInvariant(text[0])}:\\";
    }

    /// <summary>
    /// [S-1162] The <c>\LogicalDisk(*)</c> instance for a root: <c>C:\</c> is <c>C:</c>. Matched
    /// ignoring case, because the counter names the instance however the volume manager spelled it.
    /// </summary>
    public static string Instance(string root) => (Root(root) ?? root).TrimEnd('\\');

    /// <summary>
    /// [S-1162] A drive's rates out of the counter's instances. A drive the counters do not list
    /// (just mounted, or a counter that is not answering) is null — never the system-wide total,
    /// which would put the same number on every disk widget (the original's themia-releases#49).
    /// </summary>
    public static DiskRates? RatesFor(string root, IReadOnlyDictionary<string, DiskRates> byInstance)
    {
        ArgumentNullException.ThrowIfNull(byInstance);
        return byInstance.TryGetValue(Instance(root), out var rates) ? rates : null;
    }

    /// <summary>
    /// [S-1162] Pair the read and write instance lists into rates per drive letter. The
    /// <c>_Total</c> instance and anything that is not a drive (a volume mounted in a folder shows as
    /// <c>HarddiskVolume3</c>) are left out; an instance missing from one list counts as 0 there.
    /// </summary>
    public static Dictionary<string, DiskRates> Pair(
        IEnumerable<(string Instance, double Value)> reads,
        IEnumerable<(string Instance, double Value)> writes)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(writes);

        var result = new Dictionary<string, DiskRates>(StringComparer.OrdinalIgnoreCase);

        foreach (var (instance, value) in reads)
        {
            if (Root(instance) is { } root)
            {
                var key = Instance(root);
                result[key] = new DiskRates(Clean(value), result.TryGetValue(key, out var r) ? r.Write : 0);
            }
        }

        foreach (var (instance, value) in writes)
        {
            if (Root(instance) is { } root)
            {
                var key = Instance(root);
                result[key] = new DiskRates(result.TryGetValue(key, out var r) ? r.Read : 0, Clean(value));
            }
        }

        return result;
    }

    /// <summary>[S-1163] Every drive together: the sum of the drive-letter instances (never <c>_Total</c> as well, which would count twice).</summary>
    public static DiskRates Total(IReadOnlyDictionary<string, DiskRates> byInstance)
    {
        ArgumentNullException.ThrowIfNull(byInstance);

        double read = 0, write = 0;
        foreach (var rates in byInstance.Values)
        {
            read += rates.Read;
            write += rates.Write;
        }

        return new DiskRates(read, write);
    }

    /// <summary>
    /// [S-1163] The rates the chart plots for a scope: the reading's drive, every drive, or one named
    /// drive. Null when that drive has no rate (yet), which the chart shows as a gap.
    /// </summary>
    public static DiskRates? Charted(Themia.Core.Model.DiskIoScope scope, string readingRoot, string ioDrive, IReadOnlyDictionary<string, DiskRates> byInstance)
    {
        ArgumentNullException.ThrowIfNull(byInstance);

        return scope switch
        {
            Themia.Core.Model.DiskIoScope.All => byInstance.Count == 0 ? null : Total(byInstance),
            Themia.Core.Model.DiskIoScope.Drive when Root(ioDrive) is { } root => RatesFor(root, byInstance),
            _ => RatesFor(readingRoot, byInstance),
        };
    }

    /// <summary>[S-1163] What the chart's rates are of, for its caption on a tile with room: "C:", "all drives".</summary>
    public static string ChartedName(Themia.Core.Model.DiskIoScope scope, string readingRoot, string ioDrive) => scope switch
    {
        Themia.Core.Model.DiskIoScope.All => Str.WidgetsB_Disk_AllDrives.Text(),
        Themia.Core.Model.DiskIoScope.Drive when Root(ioDrive) is { } root => Instance(root),
        _ => Instance(readingRoot),
    };

    private static double Clean(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    /// <summary>[S-1161] The header title, as Explorer names a drive: "System (C:)", "Local Disk (C:)", "USB Drive (E:)".</summary>
    public static string Title(DiskVolume volume) => Title(Strings.Current, volume);

    /// <summary><see cref="Title(DiskVolume)"/> in <paramref name="locale"/>.</summary>
    public static string Title(Locale locale, DiskVolume volume)
    {
        var letter = Instance(volume.Root);
        var name = volume.Label.Trim();

        if (name.Length == 0)
        {
            name = Strings.Get(locale, volume.Removable ? Str.WidgetsB_Disk_UsbDrive : Str.WidgetsB_Disk_LocalDisk);
        }

        return Strings.Format(locale, Str.WidgetsB_Disk_Title, ("name", name), ("letter", letter));
    }

    /// <summary>A size that formats at its widest: four digits before the unit changes.</summary>
    private const double WidestBytes = 1000d * 1024 * 1024 * 1024;

    /// <summary>A rate that formats at its widest.</summary>
    private const double WidestRate = 1000d * 1024 * 1024;

    private static string Whole(Locale locale, double value)
        => LocaleFormat.Number(locale, double.IsFinite(value) && value > 0 ? Math.Round(value) : 0, 0);
}
