using System.Net.NetworkInformation;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// Network readings, from the BCL's own interface statistics.
/// <para>
/// Not <c>GetIfTable2</c>: the managed counters carry the same numbers, cost one enumeration per
/// tick, and need no P/Invoke surface to keep trim- and AOT-safe. <c>Ping</c> is in the same
/// position — a raw ICMP socket would buy nothing a widget can show.
/// </para>
/// </summary>
internal static class NetworkMetrics
{
    private const string Scope = "metrics";

    // A latency bar needs a ceiling to be a bar at all, and round-trips have no natural maximum.
    // 300 ms is the point past which a link feels broken rather than slow, so it is where the bar
    // fills.
    private const double LatencyCeilingMs = 300;

    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);

    // Throughput is a delta between two samples of cumulative counters, so the previous sample has
    // to be remembered. Single-threaded: every read happens on the UI thread's tick.
    private static long _lastSent;
    private static long _lastReceived;
    private static long _lastTicks;
    private static bool _haveNetBaseline;

    // Link speed would be the obvious denominator, but adapters report the negotiated line rate
    // (a 1 Gb NIC on a 50 Mb connection claims 1 Gb), which pins the bar near zero forever. The
    // busiest sample seen so far is at least a scale the machine actually reached.
    private static double _peakRate;

    /// <summary>
    /// Bytes per second across every active non-loopback interface.
    /// <para>
    /// The very first call has no previous sample to subtract, so it reports 0 rather than
    /// inventing a number — one tick later it is real.
    /// </para>
    /// </summary>
    public static MeterReading Network()
    {
        long sent = 0;
        long received = 0;
        var counted = 0;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Loopback and tunnel traffic is the machine talking to itself; counting it makes a
                // VPN or a local server look like network use.
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = nic.GetIPStatistics();
                sent += stats.BytesSent;
                received += stats.BytesReceived;
                counted++;
            }
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn(Scope, "could not read interface statistics", ex);
            return MeterReading.Unavailable("unavailable");
        }
        catch (PlatformNotSupportedException ex)
        {
            Log.Warn(Scope, "interface statistics unsupported", ex);
            return MeterReading.Unavailable("unavailable");
        }

        if (counted == 0)
        {
            // An offline machine has no baseline worth keeping: whatever counters come back when an
            // adapter returns are unrelated to the last ones seen.
            _haveNetBaseline = false;
            return MeterReading.Unavailable("no connection");
        }

        // Wall clock, not DateTime.Now: a clock correction between two samples would divide a real
        // byte delta by a nonsense interval and print a rate of gigabytes per second.
        var ticks = Environment.TickCount64;

        if (!_haveNetBaseline)
        {
            (_lastSent, _lastReceived, _lastTicks, _haveNetBaseline) = (sent, received, ticks, true);
            return Rates(0, 0);
        }

        var sentDelta = sent - _lastSent;
        var receivedDelta = received - _lastReceived;
        var elapsedMs = ticks - _lastTicks;

        (_lastSent, _lastReceived, _lastTicks) = (sent, received, ticks);

        // The counters are per-adapter and reset when one is disabled, reconnected or replaced, so
        // the total can fall. The delta across that boundary is not a measurement of anything —
        // report 0 and let the next sample, which has a valid baseline again, be the real one.
        if (sentDelta < 0 || receivedDelta < 0)
        {
            return Rates(0, 0);
        }

        if (elapsedMs <= 0)
        {
            return Rates(0, 0);
        }

        var seconds = elapsedMs / 1000.0;
        return Rates(receivedDelta / seconds, sentDelta / seconds);
    }

    /// <summary>Round-trip time to <paramref name="host"/>.</summary>
    public static async Task<MeterReading> PingAsync(string host, CancellationToken ct)
    {
        var target = host?.Trim() ?? string.Empty;

        if (target.Length == 0)
        {
            return MeterReading.Unavailable("no host");
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, PingTimeout, cancellationToken: ct).ConfigureAwait(false);

            if (reply.Status != IPStatus.Success)
            {
                // A timeout or an unreachable host is the normal answer to a ping, not a fault, so
                // it is shown rather than logged.
                return MeterReading.Unavailable(reply.Status == IPStatus.TimedOut ? "timed out" : "unreachable");
            }

            var ms = reply.RoundtripTime;
            return new MeterReading(Math.Clamp(ms / LatencyCeilingMs, 0, 1), $"{ms} ms", target, true);
        }
        catch (OperationCanceledException)
        {
            // ignore: the tick that asked for this reading is gone; there is nobody to show it to.
            throw;
        }
        catch (PingException ex)
        {
            Log.Warn(Scope, $"could not ping {target}", ex);
            return MeterReading.Unavailable("unreachable");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Name resolution failures arrive here rather than as a reply status.
            Log.Warn(Scope, $"could not resolve {target}", ex);
            return MeterReading.Unavailable("unknown host");
        }
    }

    /// <summary>Down and up as one reading. Down leads: it is the number that moves when a page loads.</summary>
    private static MeterReading Rates(double down, double up)
    {
        var total = down + up;
        _peakRate = Math.Max(_peakRate, total);

        // Until something has actually moved there is no scale, and dividing by a near-zero peak
        // would make idle jitter fill the bar.
        var fraction = _peakRate > 0 ? Math.Clamp(total / _peakRate, 0, 1) : 0;

        return new MeterReading(
            fraction,
            Themia.Core.Widgets.Formatting.Rate(down),
            $"up {Themia.Core.Widgets.Formatting.Rate(up)}",
            true);
    }
}
