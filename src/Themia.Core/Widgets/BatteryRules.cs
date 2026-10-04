using System.Buffers.Binary;
using System.Text;
using Themia.Core.Localization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>What <c>GetSystemPowerStatus</c> says about the battery, in the battery meter's terms.</summary>
/// <param name="HasBattery">False on a desktop (see <see cref="PowerRules.Read"/>).</param>
/// <param name="Percent">Charge 0..100, or null when Windows will not say.</param>
/// <param name="OnMains">Plugged in.</param>
/// <param name="Charging">
/// Actually taking charge (<c>BatteryFlag</c> bit 3) — not the same as plugged in: a full battery
/// on mains, or one a vendor tool holds at 80%, is plugged in and not charging.
/// </param>
/// <param name="Remaining">Windows' own estimate of the time left, only ever while discharging.</param>
public readonly record struct BatteryPower(bool HasBattery, int? Percent, bool OnMains, bool Charging, TimeSpan? Remaining);

/// <summary>
/// [S-1181] What the battery device itself reports (<c>IOCTL_BATTERY_QUERY_INFORMATION</c> /
/// <c>_STATUS</c>), summed over every battery. Every figure is null when the firmware does not
/// report it; the rows built from it then hide.
/// </summary>
/// <param name="Relative">
/// The capacities are in the battery's own relative units rather than mWh, so a ratio (health) is
/// meaningful but an energy or a power is not.
/// </param>
/// <param name="DesignCapacity">What the battery held new, mWh.</param>
/// <param name="FullCapacity">What it holds at full charge now, mWh.</param>
/// <param name="RemainingCapacity">What it holds right now, mWh.</param>
/// <param name="Rate">mW; positive while charging, negative while discharging.</param>
/// <param name="VoltageMillivolts">Terminal voltage, mV.</param>
/// <param name="Cycles">Charge cycles, where the firmware counts them.</param>
/// <param name="TemperatureCelsius">Where the firmware has a sensor.</param>
/// <param name="Chemistry">"Li-ion", "NiMH", … or null.</param>
/// <param name="Manufacturer">The pack's maker, one line, or null.</param>
/// <param name="Count">How many batteries were summed.</param>
public readonly record struct BatteryPack(
    bool Relative,
    uint? DesignCapacity,
    uint? FullCapacity,
    uint? RemainingCapacity,
    int? Rate,
    uint? VoltageMillivolts,
    uint? Cycles,
    double? TemperatureCelsius,
    string? Chemistry,
    string? Manufacturer,
    int Count);

/// <summary>[S-1181] One battery's <c>BATTERY_INFORMATION</c>, sentinels already turned into nulls.</summary>
public readonly record struct BatteryInformation(bool Relative, string? Chemistry, uint? DesignCapacity, uint? FullCapacity, uint? Cycles);

/// <summary>[S-1181] One battery's <c>BATTERY_STATUS</c>, sentinels already turned into nulls.</summary>
public readonly record struct BatteryState(uint PowerState, uint? Capacity, uint? VoltageMillivolts, int? Rate);

/// <summary>One labelled figure a battery tile can show: "Health" / "91%".</summary>
public readonly record struct BatteryLine(string Label, string Value, double? Fraction = null);

/// <summary>
/// [S-1180]..[S-1189] The battery meter's rules, as pure functions of what Windows and the battery
/// device hand back: the status sentence, what the IOCTL structures mean, health, time to full or
/// empty, and the detail lines in the order a growing tile reveals them.
/// <para>
/// The device structures are parsed here from raw bytes, bounds-checked, because they cross a
/// trust boundary (a battery driver is third-party firmware) and because every field has its own
/// "unknown" sentinel that reads as a plausible number if taken at face value — 4,294,967 Wh of
/// design capacity, a rate of −2.1 GW.
/// </para>
/// </summary>
public static class BatteryRules
{
    /// <summary><c>BATTERY_INFORMATION</c> is 36 bytes; anything shorter is not one.</summary>
    public const int InformationSize = 36;

    /// <summary><c>BATTERY_STATUS</c> is 16 bytes.</summary>
    public const int StatusSize = 16;

    /// <summary><c>BATTERY_CAPACITY_RELATIVE</c>: capacities are not in mWh.</summary>
    private const uint CapacityRelative = 0x40000000;

    /// <summary><c>BATTERY_UNKNOWN_CAPACITY</c> / <c>_VOLTAGE</c>.</summary>
    private const uint UnknownValue = 0xFFFFFFFF;

    /// <summary><c>BATTERY_UNKNOWN_RATE</c>, as the signed field reads it.</summary>
    private const int UnknownRate = int.MinValue;

    /// <summary><c>BatteryFlag</c> bit 3: charging.</summary>
    private const byte ChargingFlag = 8;

    /// <summary><c>BatteryFlag</c> = 255: Windows does not know.</summary>
    private const byte UnknownFlag = 255;

    /// <summary>
    /// [S-1180] The power status in the meter's terms. The battery/no-battery decision is
    /// <see cref="PowerRules.Read"/>'s, so the battery meter and the power tile never disagree.
    /// </summary>
    public static BatteryPower Read(byte acLineStatus, byte batteryFlag, byte batteryLifePercent, uint batteryLifeTime)
    {
        var snapshot = PowerRules.Read(acLineStatus, batteryFlag, batteryLifePercent, 0, batteryLifeTime, null);
        var charging = batteryFlag != UnknownFlag && (batteryFlag & ChargingFlag) != 0;
        return new BatteryPower(snapshot.HasBattery, snapshot.Percent, snapshot.Source == PowerSource.Mains, charging, snapshot.Remaining);
    }

    /// <summary>
    /// [S-1180] The line under the charge, the original's rules in its order: Charging; Fully charged
    /// on mains at 99% or more; "{h}h {m}m remaining" when Windows has an estimate (and the setting
    /// allows it); Plugged in; On battery.
    /// </summary>
    public static string Status(BatteryPower power, bool showRemaining) => Status(Strings.Current, power, showRemaining);

    /// <summary><see cref="Status(BatteryPower, bool)"/> in <paramref name="locale"/>.</summary>
    public static string Status(Locale locale, BatteryPower power, bool showRemaining)
    {
        if (power.Charging)
        {
            return Strings.Get(locale, Str.WidgetsB_Battery_Charging);
        }

        if (power.OnMains && power.Percent >= 99)
        {
            return Strings.Get(locale, Str.WidgetsB_Battery_FullyCharged);
        }

        if (showRemaining && power.Remaining is { } remaining && remaining > TimeSpan.Zero)
        {
            return Strings.Format(locale, Str.WidgetsB_Battery_Remaining, ("time", Hours(locale, remaining)));
        }

        return Source(locale, power);
    }

    /// <summary>"Plugged in" or "On battery".</summary>
    private static string Source(Locale locale, BatteryPower power)
        => Strings.Get(locale, power.OnMains ? Str.WidgetsB_Battery_PluggedIn : Str.WidgetsB_Battery_OnBattery);

    /// <summary>The original's "{h}h {m}m", hours always shown ("0h 25m"), whole minutes.</summary>
    public static string Hours(TimeSpan span) => Hours(Strings.Current, span);

    /// <summary><see cref="Hours(TimeSpan)"/> in <paramref name="locale"/>.</summary>
    public static string Hours(Locale locale, TimeSpan span)
    {
        var total = (long)Math.Max(0, span.TotalSeconds);
        return Strings.Format(locale, Str.WidgetsB_Units_HoursMinutes, ("hours", total / 3600), ("minutes", total % 3600 / 60));
    }

    /// <summary>[S-1187] Which of the eleven battery glyphs (empty … full) a charge draws as.</summary>
    public static int Level(int? percent)
        => percent is { } p ? (int)Math.Round(Math.Clamp(p, 0, 100) / 10.0, MidpointRounding.AwayFromZero) : 0;

    // ---- [S-1181] device structures --------------------------------------------------------------

    /// <summary>Parse a <c>BATTERY_INFORMATION</c>; null when the buffer is too short to be one.</summary>
    public static BatteryInformation? ParseInformation(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < InformationSize)
        {
            return null;
        }

        var capabilities = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var designed = BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]);
        var full = BinaryPrimitives.ReadUInt32LittleEndian(buffer[16..]);
        var cycles = BinaryPrimitives.ReadUInt32LittleEndian(buffer[32..]);

        return new BatteryInformation(
            (capabilities & CapacityRelative) != 0,
            Chemistry(buffer.Slice(8, 4)),
            Capacity(designed),
            Capacity(full),
            // Zero is what firmware without a counter reports; a battery that has genuinely done no
            // cycle yet loses nothing by not saying so.
            cycles is 0 or UnknownValue ? null : cycles);
    }

    /// <summary>Parse a <c>BATTERY_STATUS</c>; null when the buffer is too short to be one.</summary>
    public static BatteryState? ParseStatus(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < StatusSize)
        {
            return null;
        }

        var state = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        var voltage = BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]);

        return new BatteryState(
            state,
            capacity == UnknownValue ? null : capacity,
            voltage is 0 or UnknownValue ? null : voltage,
            rate == UnknownRate ? null : rate);
    }

    /// <summary>
    /// A temperature in tenths of a kelvin as °C, or null outside what a working battery can be
    /// (−40..120 °C): a zero or a sentinel otherwise reads as −273 °C.
    /// </summary>
    public static double? Celsius(uint tenthsKelvin)
    {
        var celsius = (tenthsKelvin / 10.0) - 273.15;
        return celsius is >= -40 and <= 120 ? Math.Round(celsius, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>
    /// The four ASCII bytes of <c>Chemistry</c> as a name people know. Null when they are not
    /// printable text (firmware leaves zeros).
    /// </summary>
    public static string? Chemistry(ReadOnlySpan<byte> code)
    {
        var builder = new StringBuilder(4);

        foreach (var b in code)
        {
            if (b == 0)
            {
                break;
            }

            if (b is < 0x20 or > 0x7E)
            {
                return null;
            }

            builder.Append((char)b);
        }

        var raw = builder.ToString().Trim();

        return raw.ToUpperInvariant() switch
        {
            "" => null,
            "LION" or "LI-I" or "LI I" or "LIO" => "Li-ion",
            "LIP" or "LIPO" or "LI-P" => "Li-polymer",
            "NIMH" => "NiMH",
            "NICD" => "NiCd",
            "NIZN" => "NiZn",
            "PBAC" => Str.WidgetsB_Battery_LeadAcid.Text(),
            "RAM" => Str.WidgetsB_Battery_Alkaline.Text(),
            _ => raw,
        };
    }

    /// <summary>
    /// A maker's name from the device, one printable line of at most 32 characters, or null. Driver
    /// text: anything else (control characters, an empty string) is not shown.
    /// </summary>
    public static string? Manufacturer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var clean = new string(raw.TakeWhile(c => c != '\0').Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? null : clean.Length <= 32 ? clean : clean[..32];
    }

    /// <summary>
    /// [S-1181] Every battery as one pack: capacities and rates add up (two batteries are one larger
    /// one to the person using the laptop); voltage, cycles, temperature, chemistry and maker are the
    /// first battery's that reports them. Relative units on any battery make the sum relative.
    /// </summary>
    public static BatteryPack Combine(IReadOnlyList<(BatteryInformation? Information, BatteryState? State, double? Temperature, string? Manufacturer)> batteries)
    {
        uint? design = null, full = null, remaining = null, voltage = null, cycles = null;
        int? rate = null;
        double? temperature = null;
        string? chemistry = null, manufacturer = null;
        var relative = false;

        foreach (var (information, state, temp, maker) in batteries)
        {
            if (information is { } info)
            {
                relative |= info.Relative;
                design = Add(design, info.DesignCapacity);
                full = Add(full, info.FullCapacity);
                cycles ??= info.Cycles;
                chemistry ??= info.Chemistry;
            }

            if (state is { } s)
            {
                remaining = Add(remaining, s.Capacity);
                voltage ??= s.VoltageMillivolts;
                rate = s.Rate is { } r ? (rate ?? 0) + r : rate;
            }

            temperature ??= temp;
            manufacturer ??= Manufacturer(maker);
        }

        return new BatteryPack(relative, design, full, remaining, rate, voltage, cycles, temperature, chemistry, manufacturer, batteries.Count);
    }

    // ---- [S-1182] derived figures ----------------------------------------------------------------

    /// <summary>
    /// Full-charge capacity over design capacity, as the original computed it (both must be above
    /// zero). Can exceed 1: a new pack often holds a little more than it was rated for.
    /// </summary>
    public static double? Health(BatteryPack pack)
        => pack is { DesignCapacity: > 0 and var design, FullCapacity: > 0 and var full }
            ? (double)full / design
            : null;

    /// <summary>
    /// How long until full at the present charge rate: (full − remaining) / rate. Null unless the
    /// battery is charging at a known rate in mWh terms, and past two days (a trickle, not an ETA).
    /// </summary>
    public static TimeSpan? TimeToFull(BatteryPack pack)
    {
        if (pack is not { Relative: false, Rate: > 0 and var rate, FullCapacity: { } full, RemainingCapacity: { } remaining } || remaining >= full)
        {
            return null;
        }

        return Bounded((full - remaining) / (double)rate);
    }

    /// <summary>How long until empty at the present drain: remaining / |rate|. Null unless discharging at a known rate.</summary>
    public static TimeSpan? TimeToEmpty(BatteryPack pack)
    {
        if (pack is not { Relative: false, Rate: < 0 and var rate, RemainingCapacity: { } remaining })
        {
            return null;
        }

        return Bounded(remaining / (double)-rate);
    }

    private static TimeSpan? Bounded(double hours)
        => double.IsFinite(hours) && hours > 0 && hours <= 48 ? TimeSpan.FromHours(hours) : null;

    /// <summary>"8.4 W" from milliwatts, magnitude only (the label says which way).</summary>
    public static string Watts(int milliwatts) => Watts(Strings.Current, milliwatts);

    /// <summary><see cref="Watts(int)"/> in <paramref name="locale"/>.</summary>
    public static string Watts(Locale locale, int milliwatts)
        => Strings.Format(locale, Str.WidgetsB_Units_Watts, ("value", LocaleFormat.Number(locale, Math.Abs((long)milliwatts) / 1000.0, Math.Abs((long)milliwatts) >= 100_000 ? 0 : 1)));

    /// <summary>"52.1 Wh" from milliwatt-hours.</summary>
    public static string WattHours(uint milliwattHours) => WattHours(Strings.Current, milliwattHours);

    /// <summary><see cref="WattHours(uint)"/> in <paramref name="locale"/>.</summary>
    public static string WattHours(Locale locale, uint milliwattHours)
        => Strings.Format(locale, Str.WidgetsB_Units_WattHours, ("value", Energy(locale, milliwattHours)));

    /// <summary>The number of a <see cref="WattHours(Locale, uint)"/>, without its unit.</summary>
    private static string Energy(Locale locale, uint milliwattHours)
        => LocaleFormat.Number(locale, milliwattHours / 1000.0, milliwattHours >= 100_000 ? 0 : 1);

    /// <summary>"12.38 V" from millivolts.</summary>
    public static string Volts(uint millivolts) => Volts(Strings.Current, millivolts);

    /// <summary><see cref="Volts(uint)"/> in <paramref name="locale"/>.</summary>
    public static string Volts(Locale locale, uint millivolts)
        => Strings.Format(locale, Str.WidgetsB_Units_Volts, ("value", LocaleFormat.Number(locale, millivolts / 1000.0, 2)));

    /// <summary>A whole percentage that, unlike a share, may pass 100 (health of a new pack).</summary>
    public static string WholePercent(double fraction) => WholePercent(Strings.Current, fraction);

    /// <summary><see cref="WholePercent(double)"/> in <paramref name="locale"/>.</summary>
    public static string WholePercent(Locale locale, double fraction)
        => LocaleFormat.Percent(locale, Math.Round(fraction * 100));

    /// <summary>
    /// [S-1185] How the charge moved over the chart's window: "−12% in 5m", "+8% in 20m", "No change
    /// in 5m". Null under a minute of history, where a change says nothing yet.
    /// </summary>
    public static string? Change(double firstPercent, double lastPercent, TimeSpan elapsed)
        => Change(Strings.Current, firstPercent, lastPercent, elapsed);

    /// <summary><see cref="Change(double, double, TimeSpan)"/> in <paramref name="locale"/>.</summary>
    public static string? Change(Locale locale, double firstPercent, double lastPercent, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMinutes(1) || !double.IsFinite(firstPercent) || !double.IsFinite(lastPercent))
        {
            return null;
        }

        var delta = (int)Math.Round(lastPercent - firstPercent, MidpointRounding.AwayFromZero);
        var over = Formatting.Duration(locale, elapsed);

        return delta switch
        {
            0 => Strings.Format(locale, Str.WidgetsB_Battery_NoChange, ("time", over)),
            > 0 => Strings.Format(locale, Str.WidgetsB_Battery_Gained, ("percent", LocaleFormat.Percent(locale, delta)), ("time", over)),
            _ => Strings.Format(locale, Str.WidgetsB_Battery_Lost, ("percent", LocaleFormat.Percent(locale, -delta)), ("time", over)),
        };
    }

    // ---- [S-1183] what the tile shows ------------------------------------------------------------

    /// <summary>
    /// The detail lines, in the order a growing tile reveals them: health, time to full or empty,
    /// charge rate, cycles, the change over the window, voltage, temperature, then the power
    /// source, plan and battery saver. A line whose figure the battery does not report is left out —
    /// except temperature, which is off by default and, once asked for, says "N/A" rather than
    /// vanishing (a sensor the user turned on and cannot find reads as a broken setting).
    /// </summary>
    /// <param name="change">The window's change line (<see cref="Change"/>), or null.</param>
    /// <param name="plan">The active power plan's name, or null when unknown or not wanted.</param>
    /// <param name="saver">Battery saver, or null when unknown.</param>
    public static IReadOnlyList<BatteryLine> Details(BatteryPower power, BatteryPack? pack, BatteryConfig config, string? change, string? plan, bool? saver)
        => Details(Strings.Current, power, pack, config, change, plan, saver);

    /// <summary><see cref="Details(BatteryPower, BatteryPack?, BatteryConfig, string?, string?, bool?)"/> in <paramref name="locale"/>.</summary>
    public static IReadOnlyList<BatteryLine> Details(Locale locale, BatteryPower power, BatteryPack? pack, BatteryConfig config, string? change, string? plan, bool? saver)
    {
        ArgumentNullException.ThrowIfNull(config);
        var lines = new List<BatteryLine>(10);

        if (config.ShowHealth && pack is { } p1 && Health(p1) is { } health)
        {
            lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Health), HealthText(locale, p1, health)));
        }

        if (config.ShowTimeRemaining && pack is { } p2)
        {
            if (power.Charging && TimeToFull(p2) is { } toFull)
            {
                lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_FullIn), Hours(locale, toFull)));
            }
            else if (!power.OnMains && power.Remaining is null && TimeToEmpty(p2) is { } toEmpty)
            {
                // Windows' own estimate is already the caption; this is for when it has none.
                lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_EmptyIn), Hours(locale, toEmpty)));
            }
        }

        if (config.ShowRate && pack is { Relative: false, Rate: { } rate })
        {
            lines.Add(new BatteryLine(Strings.Get(locale, rate > 0 ? Str.WidgetsB_Battery_ChargingAt : rate < 0 ? Str.WidgetsB_Battery_DrainingAt : Str.WidgetsB_Battery_Rate), Watts(locale, rate)));
        }

        if (config.ShowCycles && pack?.Cycles is { } cycles)
        {
            lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Cycles), LocaleFormat.Integer(locale, cycles)));
        }

        if (change is not null)
        {
            lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Change), change));
        }

        if (config.ShowVoltage && pack?.VoltageMillivolts is { } voltage)
        {
            lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Voltage), Volts(locale, voltage)));
        }

        if (config.ShowTemperature)
        {
            lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Meters_Temperature), pack?.TemperatureCelsius is { } t ? Strings.Format(locale, Str.WidgetsB_Units_Celsius, ("value", LocaleFormat.Number(locale, t, 0))) : Strings.Get(locale, Str.WidgetsB_Meters_NotAvailable)));
        }

        if (config.ShowPower)
        {
            lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Power), Source(locale, power)));

            if (plan is not null)
            {
                lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_PowerPlan), plan));
            }

            if (saver is { } on)
            {
                lines.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Saver), Strings.Get(locale, on ? Str.WidgetsB_Meters_On : Str.WidgetsB_Meters_Off)));
            }
        }

        return lines;
    }

    /// <summary>"91% · 52.1 of 57.0 Wh"; the energies only where they are in mWh.</summary>
    public static string HealthText(BatteryPack pack, double health) => HealthText(Strings.Current, pack, health);

    /// <summary><see cref="HealthText(BatteryPack, double)"/> in <paramref name="locale"/>.</summary>
    public static string HealthText(Locale locale, BatteryPack pack, double health)
        => pack is { Relative: false, FullCapacity: { } full, DesignCapacity: { } design }
            ? Strings.Format(locale, Str.WidgetsB_Battery_HealthOf,
                ("percent", WholePercent(locale, health)), ("full", Energy(locale, full)), ("design", WattHours(locale, design)))
            : WholePercent(locale, health);

    /// <summary>
    /// [S-1184] The capacity breakdown a large tile lists: full charge (share of design), remaining
    /// (share of full), design, then chemistry and maker. Energies only in mWh terms; empty when
    /// the battery reports none of it.
    /// </summary>
    public static IReadOnlyList<BatteryLine> Capacity(BatteryPack pack) => Capacity(Strings.Current, pack);

    /// <summary><see cref="Capacity(BatteryPack)"/> in <paramref name="locale"/>.</summary>
    public static IReadOnlyList<BatteryLine> Capacity(Locale locale, BatteryPack pack)
    {
        var rows = new List<BatteryLine>(5);

        if (!pack.Relative)
        {
            // Full charge first: a tile with room for one row shows the wear, not the rating.
            if (pack.FullCapacity is { } full)
            {
                rows.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_FullCharge), WattHours(locale, full), pack.DesignCapacity is > 0 and var d ? Math.Clamp((double)full / d, 0, 1) : null));
            }

            if (pack.RemainingCapacity is { } remaining)
            {
                rows.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_RemainingCapacity), WattHours(locale, remaining), pack.FullCapacity is > 0 and var f ? Math.Clamp((double)remaining / f, 0, 1) : null));
            }

            if (pack.DesignCapacity is { } design)
            {
                rows.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_DesignCapacity), WattHours(locale, design), 1));
            }
        }

        if (pack.Chemistry is { } chemistry)
        {
            rows.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Chemistry), chemistry));
        }

        if (pack.Manufacturer is { } maker)
        {
            rows.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Manufacturer), maker));
        }

        if (pack.Count > 1)
        {
            rows.Add(new BatteryLine(Strings.Get(locale, Str.WidgetsB_Battery_Batteries), LocaleFormat.Integer(locale, pack.Count)));
        }

        return rows;
    }

    private static uint? Capacity(uint value) => value is 0 or UnknownValue ? null : value;

    private static uint? Add(uint? total, uint? value)
        => value is { } v ? (uint)Math.Min(uint.MaxValue - 1, (long)(total ?? 0) + v) : total;
}
