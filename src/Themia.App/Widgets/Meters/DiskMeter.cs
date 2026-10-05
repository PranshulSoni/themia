using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1160]..[S-1163] Space used on one drive, with the original's 80 / 90 colours, and its reads
/// and writes as the chart (two series, scaled to what is on screen). The header names the drive as
/// Explorer does, and the card's menu opens it.
/// <para>
/// [S-1163] Space is never charted: it barely moves, and a flat line of it says nothing the number
/// does not. The chart is always reads and writes, of the drive (or drives) the settings name.
/// </para>
/// </summary>
internal sealed class DiskMeter : MeterPart
{
    private DiskConfig _config = new();
    private string? _root;
    private string? _charted;
    private IDisposable? _counters;
    private bool _preview;

    public override WidgetKind Kind => WidgetKind.Disk;

    /// <summary>[S-1163] Rates have no natural top: the window's peak, never under 1 KB/s.</summary>
    public override ChartScale Scale => ChartScale.Peak(DiskRules.ChartFloor);

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Storage, reading.Available ? reading.Fraction : null);

    public override void Bind(bool preview) => _preview = preview;

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _config = config as DiskConfig ?? new DiskConfig();

        // The counters are held only while the chart can show a rate.
        if (_config.ShowGraph && _counters is null)
        {
            _counters = DiskMetrics.Acquire();
        }
        else if (!_config.ShowGraph)
        {
            _counters?.Dispose();
            _counters = null;
        }

        // [S-1160] [S-1163] A different drive is a different measurement: the chart's samples belong
        // to the one before (the original's [S-22]). On the first config there is nothing to clear.
        _root = DiskRules.Root(_config.Drive) ?? DiskMetrics.SystemRoot;
        var previous = _charted;
        _charted = DiskRules.ChartedName(_config.IoScope, _root, _config.IoDrive);

        if (previous is null || previous == _charted)
        {
            return false;
        }

        host.ClearHistory();
        return true;
    }

    /// <summary>[S-1166] Open the drive the reading is about in Explorer.</summary>
    public override IReadOnlyList<WidgetAction> ContextActions(IMeterHost host)
    {
        var root = _root ?? DiskMetrics.SystemRoot;
        return [new WidgetAction(Glyphs.Folder, Str.WidgetsB_Disk_OpenInExplorer.Format(("drive", DiskRules.Instance(root))), () => ShellLauncher.Open(root))];
    }

    public override void Sample(IMeterHost host)
    {
        var root = _root ?? DiskMetrics.SystemRoot;

        if (Volume(root) is not { UsedFraction: { } fraction } volume)
        {
            host.SetTitle(DiskRules.Instance(root));
            host.Present(MeterReading.Unavailable(Str.WidgetsB_Disk_Unavailable.Format(("drive", DiskRules.Instance(root)))));
            return;
        }

        host.SetTitle(DiskRules.Title(volume));

        var rates = Activity() is { } activity ? DiskRules.Charted(_config.IoScope, root, _config.IoDrive, activity.Rates) : null;

        host.Present(new MeterReading(
            fraction,
            Formatting.Percent(fraction),
            true,
            // Always the read rate, never the space: the chart has no other series to fall back on.
            // No rate yet (the counters' first collection, or a drive they do not list) is a gap in
            // the chart, not a 0 that never happened.
            Value: rates?.Read ?? double.NaN,
            Second: rates?.Write));
    }

    // ---- where the figures come from: the machine, or in a gallery preview its debug fixture ----

    private DiskVolume? Volume(string root)
    {
#if DEBUG
        if (_preview && Shell.Gallery.DiskPingFixture.Volumes is { } fixture)
        {
            return fixture.Where(v => string.Equals(v.Root, root, StringComparison.OrdinalIgnoreCase)).Select(v => (DiskVolume?)v).FirstOrDefault();
        }
#endif
        DiskMetrics.Volumes(refresh: true);
        return DiskMetrics.Volume(root);
    }

    private DiskActivity? Activity()
    {
#if DEBUG
        if (_preview && Shell.Gallery.DiskPingFixture.Volumes is not null)
        {
            return Shell.Gallery.DiskPingFixture.Activity();
        }
#endif
        return _counters is null ? null : DiskMetrics.Activity();
    }

    public override void Detach()
    {
        _counters?.Dispose();
        _counters = null;
    }
}
