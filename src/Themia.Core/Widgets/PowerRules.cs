using Themia.Core.Localization;
namespace Themia.Core.Widgets;

/// <summary>Where the machine's power is coming from right now.</summary>
public enum PowerSource
{
    /// <summary>Windows says it does not know. A real answer, and not the same as "on battery".</summary>
    Unknown = 0,

    Mains = 1,
    Battery = 2,
}

/// <summary>
/// One reading of the machine's power state, in the terms the widget draws.
/// </summary>
/// <param name="Readable">
/// False when Windows would not answer at all. Every other field is then meaningless, and the tile
/// says so rather than drawing a confident "Unknown" over four rows.
/// </param>
/// <param name="Source">Mains, battery, or genuinely unknown.</param>
/// <param name="HasBattery">
/// False on a desktop. Distinct from a battery whose charge is momentarily unreadable: one is a
/// machine that has no battery meter, the other is a battery meter that cannot answer, and they
/// want different things on screen.
/// </param>
/// <param name="Percent">Charge 0..100, or null when there is no battery or Windows will not say.</param>
/// <param name="Remaining">Estimated time left on battery, or null. Windows only estimates while discharging.</param>
/// <param name="BatterySaver">Whether battery saver is on.</param>
/// <param name="PlanName">The active power plan's friendly name, or null when it could not be read.</param>
public readonly record struct PowerSnapshot(
    bool Readable,
    PowerSource Source,
    bool HasBattery,
    int? Percent,
    TimeSpan? Remaining,
    bool BatterySaver,
    string? PlanName)
{
    /// <summary>The state before the first reading lands, and the state after one fails.</summary>
    public static PowerSnapshot Unreadable { get; } =
        new(false, PowerSource.Unknown, false, null, null, false, null);
}

/// <summary>
/// What <c>GetSystemPowerStatus</c> and the power-plan API actually mean, as a pure function of the
/// bytes they hand back.
/// <para>
/// Worth its own file because the encoding is a minefield: <c>255</c> means "unknown" in three
/// different fields, <c>BatteryFlag</c> carries "no battery" in a bit rather than in the
/// percentage, and <c>BatteryLifeTime</c> uses <c>-1</c> as a sentinel in an unsigned field. Every
/// one of those reads as a plausible number if you take it at face value — 255% charge, a battery
/// on a desktop, 136 years remaining.
/// </para>
/// </summary>
public static class PowerRules
{
    /// <summary><c>BatteryFlag</c> bit 7: this machine has no system battery.</summary>
    private const byte NoSystemBattery = 128;

    /// <summary>The value all four status bytes use for "Windows does not know".</summary>
    private const byte Unknown = 255;

    /// <summary><c>SystemStatusFlag</c>: battery saver is on.</summary>
    private const byte SaverOn = 1;

    /// <summary>
    /// Turn one <c>SYSTEM_POWER_STATUS</c> plus the plan name into a snapshot.
    /// </summary>
    /// <param name="acLineStatus">0 offline, 1 online, 255 unknown.</param>
    /// <param name="batteryFlag">Bit field; bit 7 is "no system battery", 255 is unknown.</param>
    /// <param name="batteryLifePercent">0..100, or 255 for unknown.</param>
    /// <param name="systemStatusFlag">1 when battery saver is on.</param>
    /// <param name="batteryLifeTime">Seconds remaining, or 0xFFFFFFFF when unknown.</param>
    /// <param name="planName">Friendly name of the active power plan, if it could be read.</param>
    public static PowerSnapshot Read(
        byte acLineStatus,
        byte batteryFlag,
        byte batteryLifePercent,
        byte systemStatusFlag,
        uint batteryLifeTime,
        string? planName)
    {
        var source = acLineStatus switch
        {
            0 => PowerSource.Battery,
            1 => PowerSource.Mains,
            _ => PowerSource.Unknown,
        };

        // Three states packed into two bytes, and only one of them is "there is no battery".
        //
        //  - bit 7 of the flag, with a flag that is not itself the unknown sentinel and no usable
        //    percentage, is a desktop: Windows is stating the absence.
        //  - a flag of 255 is Windows saying it does not know, which is not the same claim. That
        //    machine keeps its battery row and shows "--", rather than being told it is a desktop.
        //  - a real percentage outranks the flag either way: something that reports 80% charge has
        //    a battery whatever else it set.
        var noBattery = batteryFlag != Unknown
            && (batteryFlag & NoSystemBattery) != 0
            && batteryLifePercent > 100;

        var hasBattery = !noBattery;

        int? percent = hasBattery && batteryLifePercent <= 100 ? batteryLifePercent : null;

        // Only ever offered while discharging: plugged in, Windows leaves whatever it last computed
        // in the field, and "2h 10m left" under a charging battery is simply wrong.
        TimeSpan? remaining = source == PowerSource.Battery
            && hasBattery
            && batteryLifeTime is > 0 and not uint.MaxValue
                ? TimeSpan.FromSeconds(batteryLifeTime)
                : null;

        return new PowerSnapshot(
            Readable: true,
            Source: source,
            HasBattery: hasBattery,
            Percent: percent,
            Remaining: remaining,
            BatterySaver: systemStatusFlag == SaverOn,
            PlanName: PlanDisplayName(planName));
    }

    /// <summary>
    /// The plan name, tidied, or null.
    /// <para>
    /// This crosses a trust boundary: the string comes out of the power registry, where a vendor
    /// tool or an imported scheme can have put anything, including newlines. It is collapsed to one
    /// line and length-capped so it can only ever be a row's worth of text — and it is
    /// <b>displayed</b>, never matched against, so no behaviour depends on its wording.
    /// </para>
    /// </summary>
    public static string? PlanDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return null;
        }

        var collapsed = string.Join(' ', words);
        return collapsed.Length <= 64 ? collapsed : collapsed[..64];
    }

    /// <summary>The power source, in words.</summary>
    public static string SourceLabel(PowerSnapshot snapshot) => (snapshot.Source switch
    {
        PowerSource.Mains => Str.WidgetsC_Power_Source_Mains,
        PowerSource.Battery => Str.WidgetsC_Power_Source_Battery,
        _ => Str.WidgetsC_Power_Source_Unknown,
    }).Text();

    /// <summary>The hero readout: the charge, or a dash when there is nothing to charge.</summary>
    public static string BatteryReadout(PowerSnapshot snapshot)
        => snapshot.Percent is { } percent ? LocaleFormat.Percent(Strings.Current, percent) : "--";

    /// <summary>
    /// The line under the charge: how long is left, or which way it is going. Never both, because
    /// a charging battery has no honest estimate to give.
    /// </summary>
    public static string BatteryCaption(PowerSnapshot snapshot, bool showRemaining)
    {
        if (!snapshot.HasBattery)
        {
            return Str.WidgetsC_Power_NoBattery.Text();
        }

        if (snapshot.Source == PowerSource.Mains)
        {
            return (snapshot.Percent >= 100 ? Str.WidgetsC_Power_FullyCharged : Str.WidgetsC_Power_Charging).Text();
        }

        if (showRemaining && snapshot.Remaining is { } remaining)
        {
            return Str.WidgetsC_Power_Left.Format(("duration", Formatting.Duration(remaining)));
        }

        return (snapshot.Source == PowerSource.Battery ? Str.WidgetsC_Power_Source_Battery : Str.WidgetsC_Power_Source_Unknown).Text();
    }

    /// <summary>Battery saver, in words rather than in a colour alone.</summary>
    public static string SaverLabel(PowerSnapshot snapshot) => (snapshot.BatterySaver ? Str.Common_On : Str.Common_Off).Text();

    /// <summary>The plan row's value. Says it could not be read rather than showing an empty row.</summary>
    public static string PlanLabel(PowerSnapshot snapshot) => snapshot.PlanName ?? Str.WidgetsC_Power_Unavailable.Text();

    /// <summary>
    /// The sentence a tile shows when there is nothing else to show. Only ever reached when Windows
    /// would not answer at all: a desktop with no battery still has a power source and a plan, so it
    /// is a full widget, not an empty one.
    /// </summary>
    public static string UnreadableMessage => Str.WidgetsC_Power_Unreadable.Text();
}
