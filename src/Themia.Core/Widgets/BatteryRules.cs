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
/// [S-1180] [S-1187] The battery meter's rules, as pure functions of what Windows hands back: whether
/// there is a battery, its charge, whether it is charging, and which battery glyph a charge draws as.
/// </summary>
public static class BatteryRules
{
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

    /// <summary>[S-1187] Which of the eleven battery glyphs (empty … full) a charge draws as.</summary>
    public static int Level(int? percent)
        => percent is { } p ? (int)Math.Round(Math.Clamp(p, 0, 100) / 10.0, MidpointRounding.AwayFromZero) : 0;
}
