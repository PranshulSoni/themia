using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1170]..[S-1174] Round-trip time to a host, with the original's status (a dot and Online /
/// Offline / Checking…), jitter, loss, fastest / mean / slowest answer, a chart scaled to the
/// window's slowest answer with unanswered probes on its baseline, and a Refresh command.
/// <para>
/// The one reading that cannot be taken synchronously: a probe takes up to two seconds and the tick
/// runs on the UI thread, so <see cref="Sample"/> starts a probe and the reading lands later.
/// </para>
/// </summary>
internal sealed class PingMeter : MeterPart
{
    private static readonly Brush OnlineBrush = FluentLayout.Themed("SystemFillColorSuccessBrush", Colors.Green);
    private static readonly Brush OfflineBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.Red);
    private static readonly Brush CheckingBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);

    // [S-1171] Every probe since the history started, answered or not; as long as the chart spans.
    private readonly PingHistory _history = new(60);

    private readonly Ellipse _dot = new()
    {
        Width = Fluent.Space2,
        Height = Fluent.Space2,
        VerticalAlignment = VerticalAlignment.Center,
        Fill = CheckingBrush,
    };

    private PingConfig _config = new();

    // The token source is both the cancel handle and the "a probe is already out" flag — one piece
    // of state, so the two can never disagree.
    private CancellationTokenSource? _probe;
    private string? _host;
    private int _interval;
    private string? _detail;
    private bool _detached;
    private bool _preview;

    public override WidgetKind Kind => WidgetKind.Ping;

    public override MeterUnit Unit => MeterUnit.Latency;

    /// <summary>"999 ms".</summary>
    public override double ReadingCharacters => Formatting.Latency(999).Length;

    /// <summary>"Checking…", the longest status word, so the caption never hops between probes.</summary>
    public override double CaptionCharacters(string live) => PingStats.StatusCharacters;

    /// <summary>
    /// [S-1171] The tiles carry fastest / mean / slowest from <see cref="PingStats"/>, over answers
    /// only: the view's own min / avg / max would count an unanswered probe as a 0 ms one.
    /// </summary>
    public override bool Moves => false;

    /// <summary>[S-1172] Against the slowest answer on screen, as the original scaled it (floor 1 ms).</summary>
    public override ChartScale Scale => ChartScale.Peak(1);

    public override MeterPressure PressureOf(MeterReading reading) => PingStats.Pressure(Status);

    public override FrameworkElement? HeaderTrailing => _dot;

    private PingStatus Status => PingStats.Status(_history.Any, _history.Last is not null);

    public override void Bind(bool preview) => _preview = preview;

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _detached = false;
        _config = config as PingConfig ?? new PingConfig();
        _history.Capacity = _config.GraphCapacity;

        // A new cadence restarts the view's chart ([S-1120]); the statistics start with it, so the
        // tiles never describe probes the chart no longer shows.
        if (_interval != 0 && _interval != _config.EffectiveIntervalSeconds)
        {
            _history.Clear();
        }

        _interval = _config.EffectiveIntervalSeconds;

        // [S-1173] The header names the host as the user does: its label, else its address.
        host.SetTitle(_config.EffectiveLabel ?? _config.EffectiveHost);

        var previous = _host;
        _host = _config.EffectiveHost;

        if (_host == previous)
        {
            return false;
        }

        // Before the view resubscribes, which fires a tick of its own and would start a probe on
        // the new host — cancelling afterwards would kill exactly that probe.
        CancelProbe();

        // A different host is a different measurement, so the samples already on the chart, and the
        // statistics, belong to something else. On the first config there is nothing to clear.
        _history.Clear();
        ShowStatus();

        if (previous is null)
        {
            return false;
        }

        host.ClearHistory();
        return true;
    }

    /// <summary>[S-1174] Refresh: probe now — unless one is already out, which is the answer coming.</summary>
    public override IReadOnlyList<WidgetAction> ContextActions(IMeterHost host)
        => [new WidgetAction(Glyphs.Refresh, Str.Common_Refresh.Text(), host.SampleNow)];

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

#if DEBUG
        // [S-1169] A gallery preview with the sample installed answers from it, with no packet sent.
        if (_preview && Shell.Gallery.DiskPingFixture.Ping)
        {
            Record(Shell.Gallery.DiskPingFixture.Probe(), host);
            return;
        }
#endif

        // [S-1170] Until the first probe is back the tile says so, rather than a bare "--".
        if (!_history.Any)
        {
            host.Present(new MeterReading(null, "--", PingStats.StatusText(PingStatus.Checking), false));
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
            Record(outcome.Reading is { Available: true, Value: { } ms } ? ms : null, host);
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
                Record(null, host);
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
    /// [S-1170] [S-1172] One probe into the history, and the reading it makes. An unanswered probe is
    /// a dimmed "--" over "Offline" that still plots — on the baseline, as the original drew it —
    /// so a dropout shows on the chart as a dip rather than as a gap that looks like nothing happened.
    /// </summary>
    private void Record(double? latency, IMeterHost host)
    {
        _history.Add(latency);
        ShowStatus();

        var summary = _history.Summarise();
        var status = Status;
        var caption = PingStats.StatusText(status);
        var facts = Facts(summary);
        var details = Details(summary);

        host.Present(latency is { } ms
            ? new MeterReading(
                Math.Clamp(ms / NetworkMetrics.LatencyCeilingMs, 0, 1),
                Formatting.Latency(ms),
                caption,
                true,
                Value: ms,
                Facts: facts,
                Details: details)
            : new MeterReading(0, "--", caption, false, Value: 0, Facts: facts, Details: details));
    }

    /// <summary>[S-1171] Fastest, mean, slowest: the tiles of a wide or tall tile. None before an answer.</summary>
    private IReadOnlyList<MeterFact>? Facts(PingSummary summary)
    {
        if (!_config.ShowMinAvgMax || summary is not { Min: { } min, Average: { } average, Max: { } max })
        {
            return null;
        }

        return
        [
            new MeterFact(Str.WidgetsB_Ping_Fastest.Text(), Formatting.Latency(min)),
            new MeterFact(Str.WidgetsB_Meters_Average.Text(), Formatting.Latency(average)),
            new MeterFact(Str.WidgetsB_Ping_Slowest.Text(), Formatting.Latency(max)),
        ];
    }

    /// <summary>
    /// [S-1171] [S-1173] Jitter, loss, the address behind a label, how many probes the figures cover;
    /// priority order. Jitter appears with the first answer, loss with the first probe.
    /// </summary>
    private List<MeterFact> Details(PingSummary summary)
    {
        var lines = new List<MeterFact>(4);

        if (_config.ShowJitter && summary.Jitter is { } jitter)
        {
            lines.Add(new MeterFact(Str.WidgetUi_Jitter.Text(), Formatting.Latency(jitter)));
        }

        if (_config.ShowLoss && summary.Loss is { } loss)
        {
            lines.Add(new MeterFact(Str.WidgetUi_Loss.Text(), Formatting.Percent(loss)));
        }

        // Only beside a label: without one the header already shows the address.
        if (_config.EffectiveLabel is not null)
        {
            lines.Add(new MeterFact(Str.Ping_AddressLabel.Text(), _config.EffectiveHost));
        }

        if (summary.Samples > 0)
        {
            lines.Add(new MeterFact(Str.WidgetsB_Ping_Probes.Text(), PingStats.AnsweredText(summary)));
        }

        return lines;
    }

    /// <summary>[S-1170] The header dot: success, critical, or quiet while checking.</summary>
    private void ShowStatus()
    {
        var status = Status;
        _dot.Fill = status switch
        {
            PingStatus.Online => OnlineBrush,
            PingStatus.Offline => OfflineBrush,
            _ => CheckingBrush,
        };

        ToolTipService.SetToolTip(_dot, PingStats.StatusText(status));
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
