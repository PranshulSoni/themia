using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1160]..[S-1166] Space used on a drive and how busy it is: the share in use with the original's
/// 80 / 90 colours and a used / free bar, read and write throughput as the chart (two series, scaled
/// to what is on screen) with the current rates as its caption, operations per second, the volume's
/// label and file system, and — where the tile has room — a list of drives, each opening in Explorer
/// when clicked.
/// <para>
/// [S-1163] Space is never charted: it barely moves, and a flat line of it said nothing the bar does
/// not. The chart is always reads and writes, of the drive (or drives) the settings name.
/// </para>
/// <para>
/// The first chosen drive is the reading; choosing several adds them as the drives list, which is
/// how this layout carries the original's stacked drive blocks. Used / free / total sit in the
/// tiles of a large tile, because space used barely moves and min / avg / max of it say nothing.
/// </para>
/// </summary>
internal sealed class DiskMeter : MeterPart
{
    private DiskConfig _config = new();
    private IReadOnlyList<string> _roots = [];
    private string? _primary;
    private string? _charted;
    private IDisposable? _counters;
    private bool _preview;

    public override WidgetKind Kind => WidgetKind.Disk;

    /// <summary>The chart is reads and writes: its scale line and summary speak in bytes per second.</summary>
    public override MeterUnit Unit => MeterUnit.Rate;

    /// <summary>
    /// The caption is the chart's rates ("R 1023 MB/s · W 1023 MB/s") with a chart, the space
    /// without: placed for its widest, so it does not hop as the rates change.
    /// </summary>
    public override double CaptionCharacters(string live)
        => Math.Max(live.Length, _config.ShowGraph ? DiskRules.RatesCharacters : DiskRules.FreeOfTotalCharacters);

    /// <summary>[S-1161] The used / free bar beside the chart.</summary>
    public override bool UsageBar => true;

    /// <summary>Space used barely moves: its tiles carry used / free / total instead of min / avg / max.</summary>
    public override bool Moves => false;

    /// <summary>[S-1163] Rates have no natural top: the window's peak, never under 1 KB/s.</summary>
    public override ChartScale Scale => ChartScale.Peak(DiskRules.ChartFloor);

    /// <summary>[S-1166] One row per listed drive; a ceiling, the tile shows what fits.</summary>
    public override int ListWanted => Listed().Count;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Storage, reading.Available ? reading.Fraction : null);

    public override void Bind(bool preview) => _preview = preview;

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _config = config as DiskConfig ?? new DiskConfig();
        _roots = DiskRules.Roots(_config.Drives, DiskMetrics.SystemRoot);

        // The counters are held only while something on this tile can show a rate.
        var wantsCounters = _config.ShowGraph || _config.ShowIops;

        if (wantsCounters && _counters is null)
        {
            _counters = DiskMetrics.Acquire();
        }
        else if (!wantsCounters)
        {
            _counters?.Dispose();
            _counters = null;
        }

        // [S-1160] [S-1163] A different drive is a different measurement: the chart's samples belong
        // to the one before (the original's [S-22]). On the first config there is nothing to clear.
        _primary = _roots[0];
        var previous = _charted;
        _charted = DiskRules.ChartedName(_config.IoScope, _primary, _config.IoDrive);

        if (previous is null || previous == _charted)
        {
            return false;
        }

        host.ClearHistory();
        return true;
    }

    /// <summary>[S-1166] Open the drive the reading is about in Explorer.</summary>
    public override IReadOnlyList<WidgetAction> ContextActions(IMeterHost host)
    {
        var root = _primary ?? DiskMetrics.SystemRoot;
        return [new WidgetAction(Glyphs.Folder, $"Open {DiskRules.Instance(root)} in Explorer", () => Open(root))];
    }

    public override void Sample(MeterDemand demand, IMeterHost host)
    {
        // The walk that finds new drives and their labels; at most every 30 s, whatever the cadence.
        Volumes(refresh: true);

        var root = _primary ?? DiskMetrics.SystemRoot;

        if (Volume(root) is not { UsedFraction: { } fraction } volume)
        {
            host.SetTitle(DiskRules.Instance(root));
            host.Present(MeterReading.Unavailable($"{DiskRules.Instance(root)} unavailable"));
            return;
        }

        host.SetTitle(_roots.Count > 1 ? "Disks" : DiskRules.Title(volume));

        var activity = Activity();
        var rates = activity is { } a ? DiskRules.Charted(_config.IoScope, root, _config.IoDrive, a.Rates) : null;

        // [S-1163] With a chart, the caption is the chart's: the rates now. Without one, the space.
        var caption = !_config.ShowGraph ? DiskRules.FreeOfTotal(volume)
            : rates is { } now ? DiskRules.RatesText(now)
            : "R -- · W --";

        host.Present(new MeterReading(
            fraction,
            Formatting.Percent(fraction),
            caption,
            true,
            // Always the read rate, never the space: the chart has no other series to fall back on.
            // No rate yet (the counters' first collection, or a drive they do not list) is a gap in
            // the chart, not a 0 that never happened.
            Value: rates?.Read ?? double.NaN,
            Facts:
            [
                new MeterFact("Used", Formatting.Bytes(volume.Used)),
                new MeterFact("Free", Formatting.Bytes(volume.Free)),
                new MeterFact("Total", Formatting.Bytes(volume.Total)),
            ],
            Second: rates?.Write,
            Details: demand.Details ? Details(volume, activity) : null,
            List: demand.ListRows > 0 ? DriveList(demand.ListRows, activity) : null));
    }

    /// <summary>[S-1161] [S-1163]..[S-1165] Space, whose I/O is charted, operations, the volume: priority order.</summary>
    private List<MeterFact> Details(DiskVolume volume, DiskActivity? activity)
    {
        var lines = new List<MeterFact>(4);

        // The caption carries the rates when there is a chart, so the space moves down here.
        if (_config.ShowGraph)
        {
            lines.Add(new MeterFact("Space", DiskRules.FreeOfTotal(volume)));

            // Only when the chart is not about the drive the reading is: otherwise it goes without saying.
            if (_config.IoScope != DiskIoScope.Reading)
            {
                lines.Add(new MeterFact("Chart", $"reads and writes, {DiskRules.ChartedName(_config.IoScope, volume.Root, _config.IoDrive)}"));
            }
        }

        if (_config.ShowIops && activity is { Reads: { } reads, Writes: { } writes })
        {
            lines.Add(new MeterFact("IOPS", DiskRules.OperationsText(reads, writes)));
        }

        if (_config.ShowVolume)
        {
            lines.Add(new MeterFact("Volume", DiskRules.VolumeText(volume)));
        }

        return lines;
    }

    /// <summary>[S-1166] The drives: each one's share in use (and activity), a click away from Explorer.</summary>
    private MeterList DriveList(int rows, DiskActivity? activity)
    {
        var listed = Listed();
        var shown = new List<MeterRow>(Math.Min(rows, listed.Count));

        foreach (var root in listed.Take(rows))
        {
            if (Volume(root) is not { } volume)
            {
                shown.Add(new MeterRow(DiskRules.Instance(root), "--", null, $"{DiskRules.Instance(root)} is not answering"));
                continue;
            }

            var rates = activity is { } a ? DiskRules.RatesFor(root, a.Rates) : null;

            shown.Add(new MeterRow(
                DiskRules.Title(volume),
                DiskRules.RowValue(volume, rates),
                volume.UsedFraction,
                DiskRules.RowTooltip(volume, rates),
                () => Open(volume.Root)));
        }

        return new MeterList("Drives", shown);
    }

    /// <summary>Which drives the list holds, from the last walk: cheap enough for a layout pass.</summary>
    private IReadOnlyList<string> Listed()
        => DiskRules.Listed(_config.ShowAllDrives, _roots, [.. Volumes(refresh: false).Select(v => v.Root)]);

    // ---- where the figures come from: the machine, or in a gallery preview its debug fixture ----

    private IReadOnlyList<DiskVolume> Volumes(bool refresh)
    {
#if DEBUG
        if (_preview && Shell.Gallery.DiskPingFixture.Volumes is { } fixture)
        {
            return fixture;
        }
#endif
        return DiskMetrics.Volumes(refresh);
    }

    private DiskVolume? Volume(string root)
    {
#if DEBUG
        if (_preview && Shell.Gallery.DiskPingFixture.Volumes is { } fixture)
        {
            return fixture.Where(v => string.Equals(v.Root, root, StringComparison.OrdinalIgnoreCase)).Select(v => (DiskVolume?)v).FirstOrDefault();
        }
#endif
        return DiskMetrics.Volume(root);
    }

    private DiskActivity? Activity()
    {
#if DEBUG
        if (_preview && Shell.Gallery.DiskPingFixture.Volumes is not null)
        {
            return Shell.Gallery.DiskPingFixture.Activity();
        }
#endif
        return _counters is null ? null : DiskMetrics.Activity();
    }

    /// <summary>[S-1166] Through the one launcher, which checks the path and logs a refusal.</summary>
    private static void Open(string root) => ShellLauncher.Open(root);

    public override void Detach()
    {
        _counters?.Dispose();
        _counters = null;
    }
}
