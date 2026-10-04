using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// Round-trip time to a host. The ping package (status dot, jitter / loss / min-avg-max, a
/// window-scaled chart, Refresh) adds its extras here.
/// <para>
/// The one reading that cannot be taken synchronously: a probe takes up to two seconds and the tick
/// runs on the UI thread, so <see cref="Sample"/> starts a probe and the reading lands later.
/// </para>
/// </summary>
internal sealed class PingMeter : MeterPart
{
    // The token source is both the cancel handle and the "a probe is already out" flag — one piece
    // of state, so the two can never disagree.
    private CancellationTokenSource? _probe;
    private string? _host;
    private string? _detail;
    private bool _detached;

    public override WidgetKind Kind => WidgetKind.Ping;

    public override MeterUnit Unit => MeterUnit.Latency;

    /// <summary>"999 ms".</summary>
    public override double ReadingCharacters => 6;

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _detached = false;

        var previous = _host;
        _host = (config as PingConfig)?.EffectiveHost ?? PingConfig.DefaultHost;

        if (_host == previous)
        {
            return false;
        }

        // Before the view resubscribes, which fires a tick of its own and would start a probe on
        // the new host — cancelling afterwards would kill exactly that probe.
        CancelProbe();

        // A different host is a different measurement, so the samples already on the chart belong
        // to something else. On the first config there is nothing to clear, and nothing to hurry.
        if (previous is null)
        {
            return false;
        }

        host.ClearHistory();
        return true;
    }

    /// <summary>
    /// Send one probe, unless the last one is still out.
    /// <para>
    /// A dead host takes the full two-second timeout to answer, which at a one-second cadence would
    /// mean two probes in flight, then three; the queue grows for as long as the host stays down and
    /// every reading the tile shows is older than the last tick. One at a time means a slow host
    /// simply updates more slowly, which is the truth.
    /// </para>
    /// </summary>
    public override void Sample(MeterDemand demand, IMeterHost host)
    {
        if (_probe is not null || _detached)
        {
            return;
        }

        var probe = new CancellationTokenSource();
        _probe = probe;
        _ = ProbeAsync(_host ?? PingConfig.DefaultHost, probe, host);
    }

    private async Task ProbeAsync(string target, CancellationTokenSource probe, IMeterHost host)
    {
        try
        {
            var outcome = await NetworkMetrics.PingAsync(target, probe.Token).ConfigureAwait(true);

            // A late answer from a detached view, or from a probe aimed at a host the user has
            // since changed, is not a reading — it is a stale fact about something else.
            if (_detached || !ReferenceEquals(_probe, probe))
            {
                return;
            }

            NoteProbeState(outcome.Detail);
            host.Present(outcome.Reading);
        }
        catch (OperationCanceledException)
        {
            // ignore: cancelled by Detach or by a host change, so there is nothing left to show to
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"could not probe {target}", ex);

            if (!_detached && ReferenceEquals(_probe, probe))
            {
                host.Present(MeterReading.Unavailable("error"));
            }
        }
        finally
        {
            // The task that created the source owns it, on every path: cancelling elsewhere only
            // clears the field, so exactly one Dispose can ever run for one source.
            if (ReferenceEquals(_probe, probe))
            {
                _probe = null;
            }

            probe.Dispose();
        }
    }

    /// <summary>
    /// Log a probe only when it says something the previous one did not: an unreachable host
    /// answers identically on every tick, and one line per tick would bury the rest of the log.
    /// </summary>
    private void NoteProbeState(string? detail)
    {
        if (detail == _detail)
        {
            return;
        }

        _detail = detail;

        if (detail is null)
        {
            Log.Info(Scope, $"ping answering again: {_host}");
            return;
        }

        Log.Warn(Scope, detail);
    }

    private void CancelProbe()
    {
        var probe = _probe;
        _probe = null;

        // Cancel only. Disposal belongs to ProbeAsync's finally, which runs on the continuation
        // this cancel is about to release.
        probe?.Cancel();
    }

    public override void Detach()
    {
        _detached = true;
        CancelProbe();
    }
}
