using System.Net.NetworkInformation;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// The result of one probe: what to show, plus the detail a log line would want.
/// <para>
/// Two fields rather than one because the caller logs on a <i>transition</i>, not on every probe. A
/// host that has been down for an hour is one log line, not eighteen hundred — but when that line is
/// finally written it still has to say which failure it was. <see cref="Detail"/> is null when there
/// is nothing to say, so "did this probe change anything worth writing down" is a null check rather
/// than a string comparison against the display text.
/// </para>
/// </summary>
/// <param name="Reading">What the tile shows.</param>
/// <param name="Detail">Why, in log terms, or null when the probe succeeded.</param>
internal readonly record struct PingOutcome(MeterReading Reading, string? Detail);

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
    // fills. Deliberately not the link speed: an adapter reports its negotiated line rate, which
    // says nothing about how long a packet takes to come back, and a 1 Gb NIC would pin every
    // round-trip at the bottom of the bar forever.
    internal const double LatencyCeilingMs = 300;

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

    /// <summary>
    /// Round-trip time to <paramref name="host"/>.
    /// <para>
    /// Nothing here logs. A host that is down answers the same way every ten seconds for as long as
    /// it stays down, so logging in here would be a log line per tick forever; the caller knows
    /// whether this probe said something the last one did not, and writes the line then. See
    /// <see cref="PingOutcome"/>.
    /// </para>
    /// </summary>
    public static async Task<PingOutcome> PingAsync(string host, CancellationToken ct)
    {
        var target = host?.Trim() ?? string.Empty;

        if (target.Length == 0)
        {
            return new PingOutcome(MeterReading.Unavailable("no host"), "no host configured");
        }

        // The persisted value gets the same check the settings box applies, because a hand-edited
        // state file never went through the box. See HostAddress.
        if (!HostAddress.IsPlausible(target))
        {
            return new PingOutcome(Unreachable(target, "not a host"), $"refused to probe {target}: not a plausible host");
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, PingTimeout, cancellationToken: ct).ConfigureAwait(false);

            if (reply.Status != IPStatus.Success)
            {
                // A timeout or an unreachable host is the normal answer to a ping, not a fault. It
                // is a state the tile shows, and one log line when it starts.
                var timedOut = reply.Status == IPStatus.TimedOut;

                return new PingOutcome(
                    Unreachable(target, timedOut ? "timed out" : "unreachable"),
                    $"{target}: {reply.Status}");
            }

            var ms = reply.RoundtripTime;

            return new PingOutcome(
                new MeterReading(Math.Clamp(ms / LatencyCeilingMs, 0, 1), Formatting.Latency(ms), target, true),
                null);
        }
        catch (OperationCanceledException)
        {
            // ignore: the tick that asked for this reading is gone; there is nobody to show it to.
            throw;
        }
        catch (PingException ex)
        {
            return new PingOutcome(Unreachable(target, "unreachable"), $"could not ping {target}: {ex.Message}");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Name resolution failures arrive here rather than as a reply status.
            return new PingOutcome(Unreachable(target, "unknown host"), $"could not resolve {target}: {ex.Message}");
        }
    }

    /// <summary>
    /// A failed probe, still captioned with the host.
    /// <para>
    /// <see cref="MeterReading.Unavailable"/> puts the reason in the caption, which for this widget
    /// is where the host lives — and a tile reading "--" over "timed out" does not say *what* timed
    /// out. Both go in the caption instead.
    /// </para>
    /// </summary>
    private static MeterReading Unreachable(string target, string reason)
        => new(null, "--", $"{target} · {reason}", false);

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
