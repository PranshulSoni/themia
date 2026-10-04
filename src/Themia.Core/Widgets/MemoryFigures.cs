namespace Themia.Core.Widgets;

/// <summary>
/// [S-1130] One read of the machine's memory, in bytes, as Windows reports it.
/// </summary>
/// <param name="Total">Installed physical memory the OS can use.</param>
/// <param name="Available">Physical memory available to a new allocation (standby + free).</param>
/// <param name="CommitTotal">Memory committed by every process and the kernel.</param>
/// <param name="CommitLimit">Physical memory plus every page file: the most that can be committed.</param>
/// <param name="Standby">The standby lists (cached file data that is still free to reuse), when the counters answered.</param>
/// <param name="Modified">The modified list (dirty pages waiting to be written), when the counters answered.</param>
/// <param name="PagefileUsage">Share of the page files in use, 0..1, when the counter answered.</param>
public readonly record struct MemorySnapshot(
    ulong Total,
    ulong Available,
    ulong CommitTotal,
    ulong CommitLimit,
    ulong? Standby,
    ulong? Modified,
    double? PagefileUsage);

/// <summary>The page files: how much of them is in use.</summary>
public readonly record struct PagefileFigures(ulong Used, ulong Total, double Fraction);

/// <summary>
/// [S-1131] Physical memory split the way Task Manager's composition bar splits it: in use, cached
/// (standby + modified), free. The three always add up to the total.
/// </summary>
public readonly record struct MemoryComposition(ulong InUse, ulong Cached, ulong Free);

/// <summary>
/// [S-1130] [S-1131] What the memory meter derives from a <see cref="MemorySnapshot"/>: the share in
/// use, the page file, the commit charge and the composition. Pure, so the arithmetic that is easy to
/// get subtly wrong (the page file is not <c>TotalPageFile</c>; cached is not the system cache
/// working set) is pinned by tests rather than by eye.
/// </summary>
public static class MemoryFigures
{
    /// <summary>Bytes in use: the total less what is available.</summary>
    public static ulong Used(MemorySnapshot s) => s.Total > s.Available ? s.Total - s.Available : 0;

    /// <summary>Share in use, 0..1, or null when the total is unknown.</summary>
    public static double? UsedFraction(MemorySnapshot s) => s.Total == 0 ? null : (double)Used(s) / s.Total;

    /// <summary>"10.1 GB of 16 GB" — the caption under the reading.</summary>
    public static string UsedOfTotal(MemorySnapshot s) => $"{Formatting.Bytes(Used(s))} of {Formatting.Bytes(s.Total)}";

    /// <summary>
    /// [S-1132] The page files, or null when there are none (the original hid the swap line when the
    /// total is 0). Their size is the commit limit less physical memory — <c>TotalPageFile</c> in
    /// <c>GlobalMemoryStatusEx</c> is the commit limit itself, not the page file.
    /// </summary>
    public static PagefileFigures? Pagefile(MemorySnapshot s)
    {
        if (s.CommitLimit <= s.Total || s.PagefileUsage is not { } usage || !double.IsFinite(usage))
        {
            return null;
        }

        var total = s.CommitLimit - s.Total;
        var fraction = Math.Clamp(usage, 0, 1);
        return new PagefileFigures((ulong)Math.Round(total * fraction), total, fraction);
    }

    /// <summary>"1.2 GB of 4 GB · 30%".</summary>
    public static string PagefileLine(PagefileFigures p)
        => $"{Formatting.Bytes(p.Used)} of {Formatting.Bytes(p.Total)} · {Formatting.Percent(p.Fraction)}";

    /// <summary>"18.2 GB of 31.9 GB", or null when the commit figures are missing.</summary>
    public static string? CommittedLine(MemorySnapshot s)
        => s.CommitLimit == 0 ? null : $"{Formatting.Bytes(s.CommitTotal)} of {Formatting.Bytes(s.CommitLimit)}";

    /// <summary>
    /// [S-1131] In use / cached / free, or null when the standby and modified counters did not
    /// answer (a guess would be a bar that disagrees with Task Manager). Free is what is available
    /// less the standby lists; in use is the rest less the modified list, which is cached data too.
    /// </summary>
    public static MemoryComposition? Composition(MemorySnapshot s)
    {
        if (s.Total == 0 || s.Standby is not { } standby || s.Modified is not { } modified)
        {
            return null;
        }

        var available = Math.Min(s.Available, s.Total);
        var free = available > standby ? available - standby : 0;
        var cached = Math.Min(standby + modified, s.Total - free);
        var inUse = s.Total - free - cached;
        return new MemoryComposition(inUse, cached, free);
    }
}
