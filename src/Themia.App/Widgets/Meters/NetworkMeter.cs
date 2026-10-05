using Themia.App.Services;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1190]..[S-1196] Throughput across the active adapters: download as the reading, upload in the
/// caption and as the chart's second series, both against the visible window's peak. Beside it the
/// original's extras — the connection (adapter, Wi-Fi network and signal, link speed), its address,
/// the public address (opt-in), session totals and the window's peaks — and one row per adapter.
/// </summary>
internal sealed class NetworkMeter : MeterPart
{
    /// <summary>The lowest top the chart and the bar may have: 1 KB/s, so idle chatter stays a flat line (the original's floor).</summary>
    private const double ChartFloor = 1024;

    private readonly RateWindow _window = new(60);
    private NetworkConfig _config = new();
    private int _adapters;
    private bool _preview;

    public override WidgetKind Kind => WidgetKind.Network;

    public override MeterUnit Unit => MeterUnit.Rate;

    /// <summary>"1.2 MB/s".</summary>
    public override double ReadingCharacters => 8;

    /// <summary>"↑ 105 KB/s" changes length from second to second, so it is placed at its worst case.</summary>
    public override double CaptionCharacters(string live) => Math.Max(11, live.Length);

    /// <summary>[S-1124] Against the visible window's peak, never the session's.</summary>
    public override ChartScale Scale => ChartScale.Peak(ChartFloor);

    /// <summary>[S-1190] One row per adapter that is up, to the most the tile fits.</summary>
    public override int ListWanted => _config.ShowInterfaces ? Math.Min(NetworkConfig.MaxInterfaces, _adapters) : 0;

    /// <summary>[S-1619] A preview may show the gallery's Wi-Fi fixture; a desktop tile never does.</summary>
    public override void Bind(bool preview) => _preview = preview;

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _config = config as NetworkConfig ?? new NetworkConfig();

        // [S-1191] The bar's window is the chart's: the same span, so the two agree on the top.
        _window.Capacity = _config.GraphCapacity;
        return false;
    }

    public override void Sample(MeterDemand demand, IMeterHost host)
    {
        if (NetworkThroughput.Read() is not { } sample)
        {
            host.Present(MeterReading.Unavailable(Str.WidgetsB_Meters_Unavailable.Text()));
            return;
        }

        _adapters = sample.Interfaces.Count;

        if (_adapters == 0)
        {
            _window.Clear();
            host.Present(MeterReading.Unavailable(Str.WidgetsB_Network_NoConnection.Text()));
            return;
        }

        _window.Push(sample.Down, sample.Up);

        host.Present(new MeterReading(
            _window.Fraction(sample.Down, ChartFloor),
            Formatting.Rate(sample.Down),
            $"↑ {Formatting.Rate(sample.Up)}",
            true,
            Value: sample.Down,
            Second: sample.Up,
            // [S-1127] Adapters are described (addresses, Wi-Fi) only when a line or row can show it.
            Details: demand.Details ? Details(sample) : null,
            List: demand.ListRows > 0 ? Interfaces(sample, demand.ListRows) : null));
    }

    /// <summary>
    /// [S-1193] Connection, address, public address, session, peaks — the order a growing tile
    /// reveals them. The connection is the adapter carrying the default route, busiest first.
    /// </summary>
    private List<MeterFact> Details(ThroughputSample sample)
    {
        var lines = new List<MeterFact>(6);
        var primary = Primary(sample);

        if (_config.ShowConnection && primary is { } connection)
        {
            var (rate, details) = connection;
            var name = details.Name;
            var wifi = details.Wireless ? WlanInfo.Current(rate.Id) : null;

            // [S-1611] Only a Wi-Fi line being drawn offers location consent, and only on a click.
            var hint = details.Wireless ? LocationConsent.Decide(wifi?.Ssid is not null, wifi?.Refused == true) : WifiNameDecision.None;

#if DEBUG
            if (_preview && Shell.Gallery.NetworkFixture.Wifi is { } fixture)
            {
                // [S-1619] The packaged, location-refused line, whatever this machine is joined by.
                name = Str.Shell_Adapter_Wifi.Text();
                wifi = fixture;
                hint = WifiNameRules.Decide(true, LocationPermission.Denied, lineShown: true, asked: true, hasName: false, nameRefused: true);
            }
#endif

            var text = NetworkRules.ConnectionText(wifi?.Ssid, wifi?.Signal, NetworkRules.LinkSpeed(details.LinkSpeed));
            lines.Add(new MeterFact(name, text ?? details.Type));

            if (hint.Hint)
            {
                // [S-1613] Right under the line it explains; a preview's link does nothing.
                var action = hint.Action;
                lines.Add(new MeterFact(string.Empty, WifiNameRules.Hint, _preview ? static () => { } : () => LocationConsent.Follow(action)));
            }
        }

        if (_config.ShowAddress && primary?.Details.Address is { } address)
        {
            lines.Add(new MeterFact(Str.WidgetsB_Network_IpAddress.Text(), address));
        }

        if (_config.ShowPublicAddress)
        {
            lines.Add(new MeterFact(Str.Net_PublicIp.Text(), PublicAddress.Current()));
        }

        if (_config.ShowSession)
        {
            lines.Add(new MeterFact(Str.WidgetUi_Session.Text(), NetworkRules.Totals(sample.SessionDown, sample.SessionUp)));
        }

        lines.Add(new MeterFact(Str.WidgetsB_Meters_Peak.Text(), NetworkRules.Pair(_window.PeakDown, _window.PeakUp)));
        return lines;
    }

    /// <summary>The adapter internet traffic leaves through: the busiest one with a gateway, else the busiest.</summary>
    private static (InterfaceRate Rate, AdapterDetails Details)? Primary(ThroughputSample sample)
    {
        (InterfaceRate, AdapterDetails)? first = null;

        foreach (var rate in sample.Interfaces)
        {
            if (NetworkThroughput.Describe(rate.Id) is not { } details)
            {
                continue;
            }

            if (details.HasGateway)
            {
                return (rate, details);
            }

            first ??= (rate, details);
        }

        return first;
    }

    /// <summary>
    /// [S-1190] Each adapter: its throughput (down + up — the split is on hover, because a row as
    /// narrow as a Tall tile has no room for both and a name), its share of the traffic, and the rest
    /// on hover.
    /// </summary>
    private static MeterList Interfaces(ThroughputSample sample, int rows)
    {
        var total = sample.Down + sample.Up;
        var shown = new List<MeterRow>(Math.Min(rows, sample.Interfaces.Count));

        foreach (var rate in sample.Interfaces.Take(rows))
        {
            var details = NetworkThroughput.Describe(rate.Id);
            var tooltip = details is { } d
                ? string.Join("\n", new[]
                {
                    d.Description,
                    NetworkRules.Pair(rate.Down, rate.Up),
                    NetworkRules.LinkSpeed(d.LinkSpeed) is { } speed ? $"{d.Type} · {speed}" : d.Type,
                    d.Address,
                    Str.WidgetsB_Network_SessionTotals.Format(("totals", NetworkRules.Totals(rate.SessionDown, rate.SessionUp))),
                }.Where(line => !string.IsNullOrEmpty(line)))
                : rate.Name;

            shown.Add(new MeterRow(
                rate.Name,
                Formatting.Rate(rate.Total),
                total > 0 ? rate.Total / total : 0,
                tooltip));
        }

        return new MeterList(Str.WidgetsB_Network_Adapters.Text(), shown);
    }
}
