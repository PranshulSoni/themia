using System.Globalization;
using Themia.App.Services;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1130] Physical memory in use, with the original's extra metrics: available, the page file
/// (swap), the commit charge, the processes holding the most memory, and — on a large tile — the
/// in-use / cached / free composition Task Manager draws.
/// </summary>
internal sealed class MemoryMeter : MeterPart
{
    /// <summary>
    /// [S-1133] How stale the top-process list may get. Walking every process is the most expensive
    /// read a meter makes and memory use is not a one-second story, so the list is refreshed at most
    /// this often whatever the meter's own cadence, and shared by every memory widget.
    /// </summary>
    private const long TopRefreshMs = 2000;

    private static IReadOnlyList<ProcessUsage> _top = [];
    private static long? _topAt;

    private MemoryConfig _config = new();
    private IDisposable? _counters;

    public override WidgetKind Kind => WidgetKind.Memory;

    public override int ListWanted => _config.ShowTopProcesses ? _config.EffectiveTopProcessCount : 0;

    public override bool UsageBar => _config.ShowBar;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Usage, reading.Available ? reading.Fraction : null);

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _config = config as MemoryConfig ?? new MemoryConfig();

        // The page-file and cache counters are only held while something on this tile can show them.
        var wantsCounters = _config.ShowSwap || _config.ShowBar;

        if (wantsCounters && _counters is null)
        {
            _counters = MemoryCounters.Acquire();
        }
        else if (!wantsCounters)
        {
            _counters?.Dispose();
            _counters = null;
        }

        return false;
    }

    public override void Sample(MeterDemand demand, IMeterHost host)
    {
        // The counters are worth a PDH collection only when a line or the bar that needs them is on screen.
        var detailed = (demand.Details && _config.ShowSwap) || (demand.Segments && _config.ShowBar);

        if (MemoryCounters.Read(detailed) is not { } snapshot || MemoryFigures.UsedFraction(snapshot) is not { } fraction)
        {
            host.Present(MeterReading.Unavailable(Str.WidgetsB_Meters_Unavailable.Text()));
            return;
        }

        host.Present(new MeterReading(
            fraction,
            Formatting.Percent(fraction),
            MemoryFigures.UsedOfTotal(snapshot),
            true,
            Details: Details(snapshot),
            List: demand.ListRows > 0 ? TopList(demand.ListRows, snapshot.Total) : null,
            Segments: Segments(snapshot)));
    }

    /// <summary>[S-1132] Available, then swap, then committed: the order the original listed them.</summary>
    private List<MeterFact> Details(MemorySnapshot snapshot)
    {
        var lines = new List<MeterFact>(3);

        if (_config.ShowAvailable)
        {
            lines.Add(new MeterFact(Str.WidgetUi_Available.Text(), Formatting.Bytes(snapshot.Available)));
        }

        // Hidden on a machine with no page file, as the original hid it when the total was 0.
        if (_config.ShowSwap && MemoryFigures.Pagefile(snapshot) is { } pagefile)
        {
            lines.Add(new MeterFact(Str.WidgetUi_Swap.Text(), MemoryFigures.PagefileLine(pagefile)));
        }

        if (_config.ShowCommitted && MemoryFigures.CommittedLine(snapshot) is { } committed)
        {
            lines.Add(new MeterFact(Str.WidgetsB_Memory_Committed.Text(), committed));
        }

        return lines;
    }

    /// <summary>[S-1131] In use / cached / free; null when the cache counters did not answer.</summary>
    private IReadOnlyList<MeterSegment>? Segments(MemorySnapshot snapshot)
    {
        if (!_config.ShowBar || MemoryFigures.Composition(snapshot) is not { } c)
        {
            return null;
        }

        double total = snapshot.Total;
        return
        [
            new MeterSegment(Str.WidgetsB_Memory_InUse.Text(), Formatting.Bytes(c.InUse), c.InUse / total, MeterTone.Primary),
            new MeterSegment(Str.WidgetsB_Memory_Cached.Text(), Formatting.Bytes(c.Cached), c.Cached / total, MeterTone.Secondary),
            new MeterSegment(Str.WidgetsB_Meters_Free.Text(), Formatting.Bytes(c.Free), c.Free / total, MeterTone.Rest),
        ];
    }

    /// <summary>[S-1133] The heaviest processes by working set, each with its share of physical memory.</summary>
    private static MeterList TopList(int rows, ulong total)
    {
        var now = Environment.TickCount64;

        // Always the most any tile may show, so a second, taller memory widget can share the walk.
        if (_topAt is not { } at || now - at >= TopRefreshMs || now < at)
        {
            _top = ProcessList.Top(MemoryConfig.MaxTopProcesses, ProcessSort.Memory);
            _topAt = now;
        }

        var shown = new List<MeterRow>(Math.Min(rows, _top.Count));

        foreach (var process in _top.Take(rows))
        {
            shown.Add(new MeterRow(
                process.Name,
                process.MemoryText,
                total == 0 ? null : (double)process.WorkingSetBytes / total,
                Str.WidgetsB_Meters_ProcessTooltip.Format(("name", process.Name), ("pid", process.Pid.ToString(CultureInfo.InvariantCulture)))));
        }

        return new MeterList(Str.WidgetUi_TopProcsLabel.Text(), shown);
    }

    public override void Detach()
    {
        _counters?.Dispose();
        _counters = null;
    }
}
