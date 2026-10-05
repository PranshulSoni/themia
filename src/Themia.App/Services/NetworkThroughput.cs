using System.Net.NetworkInformation;
using System.Net.Sockets;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>What a large network tile says about one adapter beyond its rates.</summary>
/// <param name="Wireless">A Wi-Fi adapter, whose network name and signal can be asked for.</param>
/// <param name="LinkSpeed">Negotiated speed in bits per second; ≤ 0 when the adapter reports none.</param>
/// <param name="Address">Its IPv4 address, else a global IPv6 one, or null.</param>
/// <param name="HasGateway">Whether traffic to the internet leaves through it.</param>
internal readonly record struct AdapterDetails(string Name, string Description, string Type, bool Wireless, long LinkSpeed, string? Address, bool HasGateway);

/// <summary>
/// [S-1190] Throughput across the machine's adapters, per adapter and in total, with session totals —
/// from the BCL's interface statistics (the same counters as <c>GetIfTable2</c>, with no P/Invoke
/// surface to keep AOT-safe).
/// <para>
/// One shared reader for every network tile: a second tile in the same tick gets this tick's sample
/// rather than a rate over a few milliseconds ([S-643]), and the session totals are the process's,
/// not a tile's. Single-threaded: every read happens on the UI thread's tick.
/// </para>
/// </summary>
internal static class NetworkThroughput
{
    private const string Scope = "metrics";

    /// <summary>[S-643] A second tile within this window reuses the first one's sample.</summary>
    private const long ReuseMs = SampleCache<int>.MinimumWindowMs;

    /// <summary>Addresses and gateways change rarely; asked for at most this often per adapter.</summary>
    private const long DetailsMs = 30_000;

    private static readonly ThroughputTracker Tracker = new();
    private static readonly Dictionary<string, NetworkInterface> Adapters = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (long At, AdapterDetails Details)> DetailCache = new(StringComparer.OrdinalIgnoreCase);

    private static ThroughputSample? _last;
    private static long? _lastAt;

    /// <summary>
    /// This tick's sample: totals, each counted adapter busiest first, and the session totals. Null
    /// when Windows would not report the statistics; an empty adapter list is "no connection".
    /// </summary>
    public static ThroughputSample? Read()
    {
        var now = Environment.TickCount64;

        if (_lastAt is { } at && now - at >= 0 && now - at < ReuseMs)
        {
            return _last;
        }

        _last = ReadNow(now);
        _lastAt = now;
        return _last;
    }

    private static ThroughputSample? ReadNow(long now)
    {
        var counters = new List<InterfaceCounters>(4);
        Adapters.Clear();

        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            var names = all.Select(n => n.Name).ToList();

            foreach (var nic in all)
            {
                // [S-1192] Loopback and tunnels are the machine talking to itself; a capture mirror
                // or a filter binding re-reports the adapter beneath it.
                if (!NetworkRules.Counts(
                        nic.OperationalStatus == OperationalStatus.Up,
                        nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel,
                        nic.Name,
                        nic.Description)
                    || NetworkRules.IsFilterBinding(nic.Name, names))
                {
                    continue;
                }

                var stats = nic.GetIPStatistics();
                counters.Add(new InterfaceCounters(nic.Id, nic.Name, stats.BytesReceived, stats.BytesSent));
                Adapters[nic.Id] = nic;
            }
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn(Scope, "could not read interface statistics", ex);
            return null;
        }
        catch (PlatformNotSupportedException ex)
        {
            Log.Warn(Scope, "interface statistics unsupported", ex);
            return null;
        }

        // Monotonic, not DateTime.Now: a clock correction between two samples would divide a real
        // byte delta by a nonsense interval.
        return Tracker.Update(counters, now);
    }

    /// <summary>
    /// [S-1193] One counted adapter's name, kind, link speed, address and whether it carries the
    /// default route; null when it is no longer present. Cached per adapter for half a minute.
    /// </summary>
    public static AdapterDetails? Describe(string id)
    {
        var now = Environment.TickCount64;

        if (DetailCache.TryGetValue(id, out var cached) && now - cached.At >= 0 && now - cached.At < DetailsMs)
        {
            return cached.Details;
        }

        if (!Adapters.TryGetValue(id, out var nic))
        {
            return null;
        }

        string? address = null;
        var gateway = false;

        try
        {
            var properties = nic.GetIPProperties();
            gateway = properties.GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any));

            // IPv4 first: it is the address people recognise. Else a global IPv6 one — never a
            // link-local fe80:: address, which says nothing about where the machine is reachable.
            var addresses = properties.UnicastAddresses.Select(u => u.Address).ToList();
            address = (addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal))?.ToString();
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn(Scope, $"could not read the addresses of {nic.Name}", ex);
        }

        long speed;

        try
        {
            speed = nic.Speed;
        }
        catch (PlatformNotSupportedException)
        {
            speed = -1; // ignore: the adapter reports no speed; the line leaves it out
        }

        var details = new AdapterDetails(
            nic.Name,
            nic.Description,
            TypeLabel(nic.NetworkInterfaceType),
            nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
            speed,
            address,
            gateway);

        DetailCache[id] = (now, details);
        return details;
    }

    /// <summary>The adapter kind, in Settings' words.</summary>
    private static string TypeLabel(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => Str.Shell_Adapter_Wifi.Text(),
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Ppp => Str.Shell_Adapter_DialUp.Text(),
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => Str.Shell_Adapter_Cellular.Text(),
        _ => Str.Shell_Adapter_Network.Text(),
    };
}
