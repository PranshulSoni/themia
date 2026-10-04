using System.Globalization;
using Themia.App.Services;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1140] Processor load across all cores, with the original's extra metrics: the clock beside
/// the reading, a bar per logical processor, temperature and fan (when a sensor answers), uptime,
/// the process / thread / handle totals, and the processes using the most processor time.
/// <para>
/// Each extra is read only while it is on screen (<see cref="MeterDemand"/>): the process walk, the
/// per-core times and the sensors cost nothing on a small tile that cannot show them.
/// </para>
/// </summary>
internal sealed class CpuMeter : MeterPart
{
    /// <summary>
    /// [S-1145] How stale the process walk may get: the most expensive read a meter makes, shared
    /// with every other process list through <see cref="ProcessList.Recent"/>.
    /// </summary>
    private static readonly TimeSpan ProcessAge = TimeSpan.FromSeconds(2);

    private CpuConfig _config = new();
    private IDisposable? _clock;
    private IDisposable? _sensors;

    public override WidgetKind Kind => WidgetKind.Cpu;

    public override double CaptionCharacters(string live) => _config.ShowFrequency ? CpuRules.FrequencyCharacters : live.Length;

    public override int ListWanted => _config.ShowTopProcesses ? _config.EffectiveTopProcessCount : 0;

    public override int CellsWanted => _config.ShowCores ? Math.Clamp(Environment.ProcessorCount, 1, 64) : 0;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Usage, reading.Available ? reading.Fraction : null);

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _config = config as CpuConfig ?? new CpuConfig();

        // The clock counter and the sensor poll are held only while this tile may show them.
        _clock = Hold(_clock, _config.ShowFrequency, ProcessorStatus.AcquireClock);
        _sensors = Hold(_sensors, _config.ShowTemperature || _config.ShowFan, CpuSensors.Acquire);
        return false;
    }

    private static IDisposable? Hold(IDisposable? lease, bool wanted, Func<IDisposable> acquire)
    {
        if (wanted)
        {
            return lease ?? acquire();
        }

        lease?.Dispose();
        return null;
    }

    public override void Sample(MeterDemand demand, IMeterHost host)
    {
        var load = SystemMetrics.Cpu();

        if (!load.Available)
        {
            host.Present(load);
            return;
        }

        var snapshot = (demand.Details && _config.ShowProcessCount) || demand.ListRows > 0
            ? ProcessList.Recent(ProcessAge)
            : null;

        host.Present(load with
        {
            Secondary = Caption(),
            Details = demand.Details ? Details(snapshot) : null,
            List = demand.ListRows > 0 && snapshot is not null ? TopList(snapshot, demand.ListRows) : null,
            Cells = demand.Cells ? Cores() : null,
        });
    }

    /// <summary>[S-1140] [S-1141] The clock when it is wanted and known, else how many processors there are.</summary>
    private string Caption()
    {
        if (_config.ShowFrequency && ProcessorStatus.Megahertz() is { } mhz)
        {
            return CpuRules.Gigahertz(mhz);
        }

        return Str.WidgetsB_Cpu_Logical.Format(("count", Environment.ProcessorCount));
    }

    /// <summary>
    /// [S-1149] The detail lines, first first: the sensors the user turned on (their reason to be
    /// here), then uptime, then the totals.
    /// </summary>
    private List<MeterFact> Details(ProcessSnapshot? snapshot)
    {
        var lines = new List<MeterFact>(5);

        if (_config.ShowTemperature || _config.ShowFan)
        {
            var sensors = CpuSensors.Read();

            if (_config.ShowTemperature && CpuRules.TemperatureText(sensors.Polled, sensors.Celsius) is { } temperature)
            {
                lines.Add(new MeterFact(Str.WidgetsB_Meters_Temperature.Text(), temperature));
            }

            if (_config.ShowFan && CpuRules.FanText(sensors.Polled, sensors.FanRpm) is { } fan)
            {
                lines.Add(new MeterFact(Str.WidgetsB_Meters_Fan.Text(), fan));
            }
        }

        if (_config.ShowUptime)
        {
            lines.Add(new MeterFact(Str.WidgetUi_Uptime.Text(), CpuRules.Uptime(TimeSpan.FromMilliseconds(Environment.TickCount64))));
        }

        if (_config.ShowProcessCount && snapshot is { Totals: { Processes: > 0 } totals })
        {
            lines.Add(new MeterFact(Str.WidgetUi_Processes.Text(), Str.WidgetsB_Cpu_Totals.Format(
                ("processes", CpuRules.Count(totals.Processes)),
                ("threads", Str.WidgetsB_Cpu_ThreadCount.Plural(totals.Threads)))));
            lines.Add(new MeterFact(Str.WidgetsB_Cpu_Handles.Text(), CpuRules.Count(totals.Handles)));
        }

        return lines;
    }

    /// <summary>[S-1145] The busiest processes, by their friendly names, each with its share of the machine.</summary>
    private static MeterList TopList(ProcessSnapshot snapshot, int rows)
    {
        var top = snapshot.Top(Math.Min(rows, CpuConfig.MaxTopProcesses), ProcessSort.Cpu);
        var shown = new List<MeterRow>(top.Count);

        foreach (var process in top)
        {
            shown.Add(new MeterRow(
                ProcessLabels.For(process),
                process.CpuText,
                process.CpuKnown ? process.CpuFraction : null,
                Str.WidgetsB_Meters_ProcessTooltipExe.Format(("name", process.Name), ("pid", process.Pid.ToString(CultureInfo.InvariantCulture)))));
        }

        return new MeterList(Str.WidgetUi_TopProcsLabel.Text(), shown);
    }

    /// <summary>[S-1142] One bar per logical processor, each in its own pressure colour.</summary>
    private static MeterCells? Cores()
    {
        var count = Math.Clamp(Environment.ProcessorCount, 1, 64);
        var title = Str.WidgetsB_Cpu_LogicalProcessors.Plural(count);

        if (ProcessorStatus.CoreLoads() is not { } loads)
        {
            // No baseline yet (the first read) or no answer: the strip keeps its place, empty.
            return new MeterCells(title, []);
        }

        var cells = new MeterCell[loads.Length];

        for (var i = 0; i < loads.Length; i++)
        {
            cells[i] = new MeterCell(loads[i], MeterPressures.For(PressureScale.Usage, loads[i]));
        }

        return new MeterCells(title, cells);
    }

    public override void Detach()
    {
        _clock?.Dispose();
        _clock = null;
        _sensors?.Dispose();
        _sensors = null;
    }
}
