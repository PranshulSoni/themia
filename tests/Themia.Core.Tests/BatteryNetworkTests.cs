using System.Buffers.Binary;
using System.Text;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The battery and network meters' rules (S-1180..S-1196).</summary>
public sealed class BatteryNetworkTests
{
    // ---- S-1180: status text --------------------------------------------------------------------

    private static BatteryPower Power(int percent, bool mains, bool charging, int? seconds = null)
        => new(true, percent, mains, charging, seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    [Theory]
    [InlineData(50, true, true, null, true, "Charging")]
    [InlineData(100, true, true, null, true, "Charging")]
    [InlineData(99, true, false, null, true, "Fully charged")]
    [InlineData(98, true, false, null, true, "Plugged in")]
    [InlineData(60, false, false, 7800, true, "2h 10m remaining")]
    [InlineData(10, false, false, 1500, true, "0h 25m remaining")]
    [InlineData(60, false, false, 7800, false, "On battery")]
    [InlineData(60, false, false, null, true, "On battery")]
    public void S1180_status_follows_the_originals_order(int percent, bool mains, bool charging, int? seconds, bool showRemaining, string expected)
        => Assert.Equal(expected, BatteryRules.Status(Power(percent, mains, charging, seconds), showRemaining));

    [Fact]
    public void S1180_charging_is_the_flag_bit_not_the_plug()
    {
        // Plugged in, flag "high" (1) without the charging bit: plugged in, not charging.
        var full = BatteryRules.Read(acLineStatus: 1, batteryFlag: 1, batteryLifePercent: 100, batteryLifeTime: uint.MaxValue);
        Assert.True(full.OnMains);
        Assert.False(full.Charging);

        var charging = BatteryRules.Read(1, 8 | 2, 40, uint.MaxValue);
        Assert.True(charging.Charging);

        // 255 is "unknown", not "every bit set".
        Assert.False(BatteryRules.Read(1, 255, 40, uint.MaxValue).Charging);
    }

    [Fact]
    public void S1180_a_desktop_has_no_battery()
    {
        var desktop = BatteryRules.Read(1, 128, 255, uint.MaxValue);
        Assert.False(desktop.HasBattery);
        Assert.Null(desktop.Percent);
    }

    // ---- S-1181: device structures --------------------------------------------------------------

    private static byte[] Information(uint capabilities, string chemistry, uint designed, uint full, uint cycles)
    {
        var buffer = new byte[BatteryRules.InformationSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, capabilities);
        Encoding.ASCII.GetBytes(chemistry).AsSpan(0, Math.Min(4, chemistry.Length)).CopyTo(buffer.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), designed);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), full);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32), cycles);
        return buffer;
    }

    private static byte[] Status(uint state, uint capacity, uint voltage, int rate)
    {
        var buffer = new byte[BatteryRules.StatusSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, state);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), capacity);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), voltage);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12), rate);
        return buffer;
    }

    [Fact]
    public void S1181_information_parses_with_its_sentinels_as_null()
    {
        var info = BatteryRules.ParseInformation(Information(0x80000000, "LION", 57_000, 51_870, 212));
        Assert.Equal(new BatteryInformation(false, "Li-ion", 57_000, 51_870, 212), info);

        var unknown = BatteryRules.ParseInformation(Information(0x40000000, "\0\0\0\0", 0xFFFFFFFF, 0, 0));
        Assert.Equal(new BatteryInformation(true, null, null, null, null), unknown);
    }

    [Fact]
    public void S1181_a_short_buffer_is_not_a_structure()
    {
        Assert.Null(BatteryRules.ParseInformation(new byte[BatteryRules.InformationSize - 1]));
        Assert.Null(BatteryRules.ParseStatus(new byte[BatteryRules.StatusSize - 1]));
        Assert.Null(BatteryRules.ParseInformation([]));
    }

    [Fact]
    public void S1181_status_parses_with_its_sentinels_as_null()
    {
        Assert.Equal(new BatteryState(2, 40_000, 12_380, -8_400), BatteryRules.ParseStatus(Status(2, 40_000, 12_380, -8_400)));
        Assert.Equal(new BatteryState(1, null, null, null), BatteryRules.ParseStatus(Status(1, 0xFFFFFFFF, 0xFFFFFFFF, int.MinValue)));
    }

    [Theory]
    [InlineData(3042u, 31.0)]
    [InlineData(2732u, 0.0)]
    [InlineData(0u, null)]
    [InlineData(4100u, null)]
    [InlineData(0xFFFFFFFFu, null)]
    public void S1181_temperature_is_tenths_of_a_kelvin_within_reason(uint tenths, double? expected)
        => Assert.Equal(expected, BatteryRules.Celsius(tenths));

    [Theory]
    [InlineData("LION", "Li-ion")]
    [InlineData("Li-I", "Li-ion")]
    [InlineData("LiP\0", "Li-polymer")]
    [InlineData("PbAc", "Lead acid")]
    [InlineData("ABCD", "ABCD")]
    public void S1181_chemistry_reads_as_a_name(string code, string expected)
        => Assert.Equal(expected, BatteryRules.Chemistry(Encoding.ASCII.GetBytes(code)));

    [Fact]
    public void S1181_chemistry_that_is_not_text_is_null()
        => Assert.Null(BatteryRules.Chemistry([0x01, 0xFF, 0x41, 0x42]));

    [Fact]
    public void S1181_two_batteries_add_up_and_take_the_first_reported_figures()
    {
        var a = (new BatteryInformation(false, "Li-ion", 24_000, 22_000, null), new BatteryState(2, 10_000, 11_000, -4_000), (double?)null, "Maker\0junk");
        var b = (new BatteryInformation(false, null, 48_000, 45_000, 80u), new BatteryState(2, 30_000, 12_000, -6_000), (double?)30.5, "Other");

        var pack = BatteryRules.Combine([a, b]);

        Assert.Equal(72_000u, pack.DesignCapacity);
        Assert.Equal(67_000u, pack.FullCapacity);
        Assert.Equal(40_000u, pack.RemainingCapacity);
        Assert.Equal(-10_000, pack.Rate);
        Assert.Equal(11_000u, pack.VoltageMillivolts);
        Assert.Equal(80u, pack.Cycles);
        Assert.Equal(30.5, pack.TemperatureCelsius);
        Assert.Equal("Li-ion", pack.Chemistry);
        Assert.Equal("Maker", pack.Manufacturer);
        Assert.Equal(2, pack.Count);
    }

    // ---- S-1182: derived figures ----------------------------------------------------------------

    private static BatteryPack Pack(uint? design = 57_000, uint? full = 51_870, uint? remaining = 40_000, int? rate = -8_400, bool relative = false)
        => new(relative, design, full, remaining, rate, 12_380, 212, null, "Li-ion", null, 1);

    [Fact]
    public void S1182_health_is_full_over_design_and_needs_both()
    {
        Assert.Equal(0.91, BatteryRules.Health(Pack())!.Value, 3);
        Assert.Null(BatteryRules.Health(Pack(design: null)));
        Assert.Null(BatteryRules.Health(Pack(full: null)));
        Assert.Equal(1.02, BatteryRules.Health(Pack(design: 50_000, full: 51_000))!.Value, 3);
    }

    [Fact]
    public void S1182_time_to_full_and_empty_follow_the_rate()
    {
        Assert.Equal(TimeSpan.FromHours(1), BatteryRules.TimeToFull(Pack(full: 50_000, remaining: 40_000, rate: 10_000)));
        Assert.Equal(TimeSpan.FromHours(5), BatteryRules.TimeToEmpty(Pack(remaining: 40_000, rate: -8_000)));

        // Wrong direction, unknown rate, relative units, already full, or a trickle: no estimate.
        Assert.Null(BatteryRules.TimeToFull(Pack(rate: -8_000)));
        Assert.Null(BatteryRules.TimeToEmpty(Pack(rate: 8_000)));
        Assert.Null(BatteryRules.TimeToEmpty(Pack(rate: null)));
        Assert.Null(BatteryRules.TimeToFull(Pack(rate: 10_000, relative: true)));
        Assert.Null(BatteryRules.TimeToFull(Pack(full: 50_000, remaining: 50_000, rate: 1_000)));
        Assert.Null(BatteryRules.TimeToFull(Pack(full: 50_000, remaining: 0, rate: 1)));
    }

    [Theory]
    [InlineData(-8_400, "8.4 W")]
    [InlineData(65_000, "65.0 W")]
    [InlineData(120_000, "120 W")]
    [InlineData(0, "0.0 W")]
    public void S1182_watts(int milliwatts, string expected) => Assert.Equal(expected, BatteryRules.Watts(milliwatts));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(73, 7)]
    [InlineData(100, 10)]
    public void S1187_icon_level_is_the_nearest_tenth(int? percent, int expected) => Assert.Equal(expected, BatteryRules.Level(percent));

    // ---- S-1183: detail lines -------------------------------------------------------------------

    [Fact]
    public void S1183_details_come_in_ladder_order_and_hide_what_is_not_reported()
    {
        var config = new BatteryConfig();
        var lines = BatteryRules.Details(Power(73, false, false, 7800), Pack(), config, "−12% in 5m", "Balanced", false);

        Assert.Equal(
            ["Health", "Draining at", "Cycles", "Change", "Voltage", "Power", "Power plan", "Battery saver"],
            lines.Select(l => l.Label).ToArray());
        Assert.Equal("91% · 51.9 of 57.0 Wh", lines[0].Value);

        // No device figures: only what GetSystemPowerStatus knows.
        var bare = BatteryRules.Details(Power(73, false, false), null, config, null, null, null);
        Assert.Equal(["Power"], bare.Select(l => l.Label).ToArray());
    }

    [Fact]
    public void S1183_time_to_full_while_charging_and_own_estimate_when_windows_has_none()
    {
        var config = new BatteryConfig();
        var charging = BatteryRules.Details(Power(70, true, true), Pack(full: 50_000, remaining: 40_000, rate: 10_000), config, null, null, null);
        Assert.Contains(new BatteryLine("Full in", "1h 0m"), charging);
        Assert.Contains(new BatteryLine("Charging at", "10.0 W"), charging);

        var noEstimate = BatteryRules.Details(Power(70, false, false), Pack(remaining: 40_000, rate: -8_000), config, null, null, null);
        Assert.Contains(new BatteryLine("Empty in", "5h 0m"), noEstimate);

        // Windows' estimate is already the caption: no second one.
        var estimated = BatteryRules.Details(Power(70, false, false, 7800), Pack(remaining: 40_000, rate: -8_000), config, null, null, null);
        Assert.DoesNotContain(estimated, l => l.Label == "Empty in");
    }

    [Fact]
    public void S1183_toggles_remove_lines_and_temperature_says_NA_once_asked_for()
    {
        var config = new BatteryConfig { ShowHealth = false, ShowRate = false, ShowCycles = false, ShowVoltage = false, ShowPower = false, ShowTemperature = true };
        var lines = BatteryRules.Details(Power(73, false, false, 7800), Pack(), config, null, "Balanced", true);
        Assert.Equal([new BatteryLine("Temperature", "N/A")], lines);
    }

    [Fact]
    public void S1184_capacity_rows_carry_shares_and_skip_energies_in_relative_units()
    {
        var rows = BatteryRules.Capacity(Pack());
        Assert.Equal(["Full charge", "Remaining", "Design capacity", "Chemistry"], rows.Select(r => r.Label).ToArray());
        Assert.Equal(0.91, rows[0].Fraction!.Value, 2);

        var relative = BatteryRules.Capacity(Pack(relative: true));
        Assert.Equal(["Chemistry"], relative.Select(r => r.Label).ToArray());
    }

    [Theory]
    [InlineData(80, 68, 300, "−12% in 5m")]
    [InlineData(40, 48, 1200, "+8% in 20m")]
    [InlineData(50, 50, 120, "No change in 2m")]
    public void S1185_change_over_the_window(double first, double last, int seconds, string expected)
        => Assert.Equal(expected, BatteryRules.Change(first, last, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void S1185_under_a_minute_says_nothing()
        => Assert.Null(BatteryRules.Change(80, 70, TimeSpan.FromSeconds(59)));

    // ---- S-1190: throughput ---------------------------------------------------------------------

    [Fact]
    public void S1190_rates_are_per_adapter_deltas_over_elapsed_time()
    {
        var tracker = new ThroughputTracker();
        var first = tracker.Update([new("a", "Wi-Fi", 1_000, 500), new("b", "Ethernet", 0, 0)], 0);
        Assert.Equal(0, first.Down);

        var second = tracker.Update([new("a", "Wi-Fi", 3_000, 1_500), new("b", "Ethernet", 4_000, 0)], 2_000);
        Assert.Equal(3_000, second.Down);
        Assert.Equal(500, second.Up);
        Assert.Equal("Ethernet", second.Interfaces[0].Name);
        Assert.Equal(6_000, second.SessionDown);
        Assert.Equal(1_000, second.SessionUp);
    }

    [Fact]
    public void S1190_one_adapter_resetting_does_not_zero_the_others()
    {
        var tracker = new ThroughputTracker();
        tracker.Update([new("a", "Wi-Fi", 10_000, 0), new("b", "Ethernet", 10_000, 0)], 0);
        var reset = tracker.Update([new("a", "Wi-Fi", 100, 0), new("b", "Ethernet", 12_000, 0)], 1_000);

        Assert.Equal(2_000, reset.Down);
        Assert.Equal(2_000, reset.SessionDown);

        // The reset adapter counts again from its new baseline, and the session never goes back.
        var after = tracker.Update([new("a", "Wi-Fi", 1_100, 0), new("b", "Ethernet", 12_000, 0)], 2_000);
        Assert.Equal(1_000, after.Down);
        Assert.Equal(3_000, after.SessionDown);
    }

    [Fact]
    public void S1190_a_new_adapter_starts_from_a_baseline_not_its_lifetime()
    {
        var tracker = new ThroughputTracker();
        tracker.Update([new("a", "Wi-Fi", 0, 0)], 0);
        var joined = tracker.Update([new("a", "Wi-Fi", 0, 0), new("b", "Ethernet", 9_000_000_000, 0)], 1_000);
        Assert.Equal(0, joined.Down);
    }

    [Fact]
    public void S1190_no_elapsed_time_is_no_rate()
    {
        var tracker = new ThroughputTracker();
        tracker.Update([new("a", "Wi-Fi", 0, 0)], 1_000);
        var same = tracker.Update([new("a", "Wi-Fi", 5_000, 0)], 1_000);
        Assert.Equal(0, same.Down);
        Assert.Equal(5_000, same.SessionDown);
    }

    // ---- S-1191: bar scale ----------------------------------------------------------------------

    [Fact]
    public void S1191_the_bar_is_scaled_to_the_window_not_the_session()
    {
        var window = new RateWindow(3);
        window.Push(1_000_000, 0);
        Assert.Equal(1, window.Fraction(1_000_000, 1024));

        window.Push(10_000, 0);
        window.Push(10_000, 0);
        window.Push(10_000, 5_000);

        // The burst has left the window: the top is now the busiest of the last three.
        Assert.Equal(1, window.Fraction(10_000, 1024));
        Assert.Equal(10_000, window.PeakDown);
        Assert.Equal(5_000, window.PeakUp);
    }

    [Fact]
    public void S1191_idle_stays_flat_against_the_floor()
    {
        var window = new RateWindow(10);
        window.Push(100, 50);
        Assert.Equal(100 / 1024.0, window.Fraction(100, 1024), 6);
    }

    [Fact]
    public void S1191_upload_shares_the_top()
    {
        var window = new RateWindow(10);
        window.Push(1_000, 4_000);
        Assert.Equal(0.25, window.Fraction(1_000, 1024), 6);
    }

    // ---- S-1192: what counts --------------------------------------------------------------------

    [Theory]
    [InlineData("Ethernet-Npcap Packet Driver (NPCAP)-0000", "Intel(R) Ethernet", true)]
    [InlineData("Ethernet", "Realtek PCIe GbE-WinPcap Packet Driver", true)]
    [InlineData("Npcap Loopback Adapter", "Npcap Loopback Adapter", false)]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211", false)]
    public void S1192_capture_mirrors_are_not_counted(string name, string description, bool mirror)
    {
        Assert.Equal(mirror, NetworkRules.IsCaptureMirror(name, description));
        Assert.Equal(!mirror, NetworkRules.Counts(true, false, name, description));
    }

    [Theory]
    [InlineData("WiFi-QoS Packet Scheduler-0000", true)]
    [InlineData("WiFi-WFP Native MAC Layer LightWeight Filter-0000", true)]
    [InlineData("wifi-Baumer Packet Filter Driver-0001", true)]
    [InlineData("WiFi", false)]
    [InlineData("Lab-0001", false)]
    [InlineData("WiFi-0000", false)]
    [InlineData("Ethernet-Npcap Packet Driver (NPCAP)", false)]
    [InlineData("Local Area Connection* 10", false)]
    public void S1192_filter_bindings_of_another_adapter_are_not_counted(string name, bool binding)
        => Assert.Equal(binding, NetworkRules.IsFilterBinding(name, ["WiFi", "Ethernet", "Lab-0001", "Local Area Connection* 10", name]));

    [Fact]
    public void S1192_down_loopback_and_tunnels_are_not_counted()
    {
        Assert.False(NetworkRules.Counts(false, false, "Ethernet", "x"));
        Assert.False(NetworkRules.Counts(true, true, "Loopback", "x"));
    }

    // ---- S-1193 / S-1194: details ---------------------------------------------------------------

    [Theory]
    [InlineData(1_000_000_000L, "1 Gbps")]
    [InlineData(2_500_000_000L, "2.5 Gbps")]
    [InlineData(866_700_000L, "867 Mbps")]
    [InlineData(100_000_000L, "100 Mbps")]
    [InlineData(56_000L, "56 Kbps")]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    public void S1193_link_speed_reads_like_settings(long bits, string? expected) => Assert.Equal(expected, NetworkRules.LinkSpeed(bits));

    [Fact]
    public void S1193_ssid_is_printable_utf8_or_nothing()
    {
        Assert.Equal("HomeNet", NetworkRules.Ssid("HomeNet"u8));
        Assert.Equal("Café", NetworkRules.Ssid("Café"u8));
        Assert.Null(NetworkRules.Ssid([]));
        Assert.Null(NetworkRules.Ssid([0xFF, 0xFE]));
        Assert.Null(NetworkRules.Ssid([0x41, 0x00, 0x42]));
        Assert.Null(NetworkRules.Ssid(new byte[33]));
    }

    [Fact]
    public void S1193_connection_line_joins_what_is_known()
    {
        Assert.Equal("HomeNet · 82% · 867 Mbps", NetworkRules.ConnectionText("HomeNet", 82, "867 Mbps"));
        Assert.Equal("1 Gbps", NetworkRules.ConnectionText(null, null, "1 Gbps"));
        Assert.Null(NetworkRules.ConnectionText(null, 101, null));
    }

    [Theory]
    [InlineData("203.0.113.7\n", "203.0.113.7")]
    [InlineData(" 2001:db8::1 ", "2001:db8::1")]
    [InlineData("<html>captive portal</html>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("999.1.1.1", null)]
    public void S1194_public_address_is_validated(string? body, string? expected) => Assert.Equal(expected, NetworkRules.PublicAddress(body));

    [Fact]
    public void S1195_network_figures_default_on_except_the_public_address()
    {
        var config = new NetworkConfig();
        Assert.True(config.ShowConnection && config.ShowAddress && config.ShowSession && config.ShowInterfaces);
        Assert.False(config.ShowPublicAddress);

        var copy = (NetworkConfig)new NetworkConfig { ShowPublicAddress = true, ShowSession = false }.Clone();
        Assert.True(copy.ShowPublicAddress);
        Assert.False(copy.ShowSession);
    }

    [Fact]
    public void S1183_battery_figures_default_on_except_temperature_and_clone()
    {
        var config = new BatteryConfig();
        Assert.True(config.ShowHealth && config.ShowRate && config.ShowCycles && config.ShowVoltage && config.ShowPower && config.ShowTimeRemaining);
        Assert.False(config.ShowTemperature);

        var copy = (BatteryConfig)new BatteryConfig { ShowTemperature = true, ShowCycles = false }.Clone();
        Assert.True(copy.ShowTemperature);
        Assert.False(copy.ShowCycles);
    }
}
