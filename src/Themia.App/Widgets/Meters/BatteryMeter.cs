using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1180]..[S-1187] Charge level, with the original's status sentence and battery icon, and what
/// the battery device reports beside it: health (full against design capacity), time to full or
/// empty, charge rate, cycles, voltage, temperature, the change over the chart's window, the power
/// source, plan and battery saver, and — on a large tile — the capacity breakdown as a list. Its
/// history is drawn like any other meter's when the graph is on ([S-1126]).
/// </summary>
internal sealed class BatteryMeter : MeterPart
{
    /// <summary>The breakdown is short; a ceiling keeps a misreporting driver from filling a tile.</summary>
    private const int MaxCapacityRows = 6;

    // Segoe Fluent Icons: Battery0..Battery9, Battery10; BatteryCharging0..8, BatteryCharging9 (also
    // drawn for a full charging battery — the font has no charging-10 in the same family).
    private static readonly string[] Levels =
        ["", "", "", "", "", "", "", "", "", "", ""];

    private static readonly string[] ChargingLevels =
        ["", "", "", "", "", "", "", "", "", "", ""];

    /// <summary>[S-1185] (tick, percent) over the chart's span, for the "change" line.</summary>
    private readonly Queue<(long At, double Percent)> _history = new();

    private BatteryConfig _config = new();
    private bool _charging;
    private int? _percent;
    private int _capacityRows;
    private bool _preview;

    public override WidgetKind Kind => WidgetKind.Battery;

    public override bool Moves => false;

    public override bool CanBeAbsent => true;

    /// <summary>[S-1184] The capacity breakdown, when the battery reports one.</summary>
    public override int ListWanted => _capacityRows;

    /// <summary>The icon sits beside the number, about two characters wide at the reading's size.</summary>
    public override double ReadingCharacters => 6;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Charge, reading.Available ? reading.Fraction : null, _charging);

    /// <summary>[S-1187] The battery glyph, filled to the nearest tenth, with a bolt while charging.</summary>
    public override string? Badge(MeterReading reading)
        => reading.Available && _percent is { } percent
            ? (_charging ? ChargingLevels : Levels)[BatteryRules.Level(percent)]
            : null;

    public override void Bind(bool preview) => _preview = preview;

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _config = config as BatteryConfig ?? new BatteryConfig();

        // A toggle changes what the reading carries, which is built at sample time.
        return true;
    }

    public override void Sample(MeterDemand demand, IMeterHost host)
    {
        if (Power() is not { } power)
        {
            host.Present(MeterReading.Unavailable("unavailable"));
            return;
        }

        if (!power.HasBattery)
        {
            _history.Clear();
            host.Present(MeterReading.Missing("No battery"));
            return;
        }

        if (power.Percent is not { } percent)
        {
            host.Present(MeterReading.Unavailable("unavailable"));
            return;
        }

        _charging = power.Charging;
        _percent = percent;

        var pack = Pack();
        var capacity = pack is { } p ? BatteryRules.Capacity(p) : [];
        _capacityRows = Math.Min(MaxCapacityRows, capacity.Count);

        // The plan is a registry read: only when a detail line can show it.
        string? plan = null;
        bool? saver = null;

        if (demand.Details && _config.ShowPower && Plan() is { Readable: true } snapshot)
        {
            plan = snapshot.PlanName;
            saver = snapshot.BatterySaver;
        }

        var details = BatteryRules.Details(power, pack, _config, Change(percent), plan, saver)
            .Select(line => new MeterFact(line.Label, line.Value))
            .ToList();

        var list = demand.ListRows > 0 && capacity.Count > 0
            ? new MeterList("Capacity", capacity.Take(demand.ListRows).Select(row => new MeterRow(row.Label, row.Value, row.Fraction)).ToList())
            : null;

        host.Present(new MeterReading(
            percent / 100.0,
            $"{percent}%",
            BatteryRules.Status(power, _config.ShowTimeRemaining),
            true,
            Details: details,
            List: list));
    }

    /// <summary>[S-1185] How the charge moved over the chart's span, oldest sample to newest.</summary>
    private string? Change(int percent)
    {
        var now = Environment.TickCount64;
        var span = _config.GraphSpanSeconds * 1000L;

        _history.Enqueue((now, percent));

        while (_history.Count > 0 && now - _history.Peek().At > span)
        {
            _history.Dequeue();
        }

        if (_history.Count < 2)
        {
            return null;
        }

        var first = _history.Peek();
        return BatteryRules.Change(first.Percent, percent, TimeSpan.FromMilliseconds(now - first.At));
    }

    private BatteryPower? Power()
    {
#if DEBUG
        if (_preview && Shell.Gallery.BatteryFixture.Power() is { } fixture)
        {
            return fixture;
        }
#endif
        return SystemMetrics.Battery();
    }

    private BatteryPack? Pack()
    {
#if DEBUG
        if (_preview && Shell.Gallery.BatteryFixture.Pack is { } fixture)
        {
            return fixture;
        }
#endif
        return BatteryDevice.Read();
    }

    private PowerSnapshot? Plan()
    {
#if DEBUG
        if (_preview && Shell.Gallery.BatteryFixture.Pack is not null)
        {
            return PowerRules.Read(1, 9, 64, 0, uint.MaxValue, "Balanced");
        }
#endif
        return PowerStatus.Read();
    }
}
