using Themia.App.Services.Web;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>What the sensors last said, and whether they have been asked yet.</summary>
/// <param name="Polled">False until the first poll answered: a tile shows nothing rather than flashing "N/A".</param>
internal readonly record struct CpuSensorState(bool Polled, double? Celsius, double? FanRpm);

/// <summary>
/// [S-1146] [S-1147] [S-1148] The processor temperature and fan, from the two sources the original
/// read, in its order — without WMI, which Native AOT cannot reach:
/// <list type="number">
/// <item>LibreHardwareMonitor's (or OpenHardwareMonitor's) local web server,
/// <c>http://localhost:8085/data.json</c> — opt-in on the user's side (they run the monitor and turn
/// its server on), the only fan source on Windows, and the on-die reading AMD boards hide from ACPI;</item>
/// <item>the ACPI thermal zone, through PDH's <c>\Thermal Zone Information(*)\Temperature</c> (Kelvin):
/// the same zone the original fell back to, and as often missing.</item>
/// </list>
/// <para>
/// Polled only while a widget with temperature or fan turned on holds a lease: every
/// <see cref="ActivePeriodMs"/> while either source answers, backing off to
/// <see cref="IdlePeriodMs"/> while neither does, so a machine without sensors is asked once a
/// minute. Single-flight: a poll still out is never joined by a second. The web request is awaited
/// from the UI thread's tick and resumes on it, so the state below is only touched from that thread.
/// </para>
/// </summary>
internal static class CpuSensors
{
    private const string Scope = "cpu-sensors";

    /// <summary>The monitor's default port; the web server has to be turned on in its Options menu.</summary>
    private static readonly Uri MonitorUri = new("http://localhost:8085/data.json");

    private const long ActivePeriodMs = 5_000;
    private const long IdlePeriodMs = 60_000;

    private static int _leases;
    private static CpuSensorState _state;
    private static long? _polledAt;
    private static bool _inFlight;
    private static bool _anyAnswered;
    private static CancellationTokenSource? _cancel;

    private static PdhQuery? _zoneQuery;
    private static PdhCounter? _zones;
    private static bool _zoneQueryFailed;

    /// <summary>Start polling (if this is the first holder); dispose to stop.</summary>
    public static IDisposable Acquire()
    {
        _leases++;
        _cancel ??= new CancellationTokenSource();
        return new Lease();
    }

    /// <summary>The latest readings; starts a poll when one is due. Never blocks the tick on the network.</summary>
    public static CpuSensorState Read()
    {
        if (_leases == 0)
        {
            return _state;
        }

        var now = Environment.TickCount64;
        var period = _anyAnswered ? ActivePeriodMs : IdlePeriodMs;

        if (!_inFlight && (_polledAt is not { } at || now - at >= period || now < at))
        {
            _ = Poll();
        }

        return _state;
    }

    private static async Task Poll()
    {
        _inFlight = true;
        var token = _cancel?.Token ?? CancellationToken.None;

        try
        {
            // The thermal zone is a local read and answers at once; the monitor is a local round trip.
            var zone = ReadZone();
            var monitor = await ReadMonitor(token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            var celsius = monitor.Celsius ?? zone;
            _anyAnswered = celsius is not null || monitor.FanRpm is not null;
            _state = new CpuSensorState(true, celsius, monitor.FanRpm);
        }
        catch (OperationCanceledException)
        {
            // ignore: the last widget let go mid-poll; nobody is waiting for the answer
        }
        catch (Exception ex)
        {
            // Fire-and-forget from a tick: an escape here would go unobserved, so it is logged and
            // the tile keeps its last reading until the next poll.
            Log.Warn(Scope, "could not read the processor sensors", ex);
        }
        finally
        {
            // A poll cut short by the last release does not count as one: the next holder asks at once.
            if (!token.IsCancellationRequested)
            {
                _polledAt = Environment.TickCount64;
            }

            _inFlight = false;
        }
    }

    private static async Task<SensorReadout> ReadMonitor(CancellationToken token)
    {
        var response = await WebFetch.GetAsync(MonitorUri, "application/json", token, NetReach.ThisMachine);

        // Not running, or its web server is off: the common case, and not worth a log line per minute.
        return response.Status == FetchStatus.Ok ? HardwareMonitor.Parse(response.Body) : default;
    }

    private static double? ReadZone()
    {
        if (_zoneQuery is null && !_zoneQueryFailed)
        {
            _zoneQuery = PdhQuery.Open(Scope);
            _zoneQueryFailed = _zoneQuery is null;
            _zones = _zoneQuery?.Add(@"\Thermal Zone Information(*)\Temperature");

            if (_zoneQuery is not null && _zones is null)
            {
                Log.Info(Scope, "this machine reports no ACPI thermal zone; temperature needs a hardware monitor");
            }
        }

        if (_zoneQuery is not { } query || _zones is not { } zones)
        {
            return null;
        }

        query.Collect();
        return CpuRules.HottestZone(query.Doubles(zones).Select(z => z.Value));
    }

    private static void Release()
    {
        _leases = Math.Max(0, _leases - 1);

        if (_leases > 0)
        {
            return;
        }

        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;

        _zoneQuery?.Dispose();
        _zoneQuery = null;
        _zones = null;
        _zoneQueryFailed = false;

        // The next holder starts from "not asked yet", so it does not show a stale reading as current.
        _state = default;
        _polledAt = null;
        _anyAnswered = false;
    }

    /// <summary>One widget's hold on the sensors. Disposing twice releases once.</summary>
    private sealed class Lease : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Release();
        }
    }
}
