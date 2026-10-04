using Themia.App.Services.Web;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// [S-1194] The machine's public IP address, as the original asked for it: one plain-text request to
/// <c>api.ipify.org</c>, at most every ten minutes, shared by every network tile.
/// <para>
/// Opt-in (a third party learns the address and that Themia is running; the settings row says so),
/// asked only while a tile that wants it is showing its details, and single-flight: a request that
/// is out is never sent again beside itself. The answer is validated as one IP address
/// (<see cref="NetworkRules.PublicAddress"/>) before it is shown. Runs on the UI thread except the
/// request itself; the result is picked up by the next sample.
/// </para>
/// </summary>
internal static class PublicAddress
{
    private const string Scope = "metrics";

    private static readonly Uri Service = new("https://api.ipify.org");

    private static string? _address;
    private static long? _askedAt;
    private static bool _failed;
    private static Task? _inFlight;

    /// <summary>
    /// The address, or the state to show instead: "Checking…" before the first answer,
    /// "Unavailable" after a failure. Asking is what starts (or refreshes) the fetch.
    /// </summary>
    public static string Current()
    {
        var now = Environment.TickCount64;

        // A task that finished before it was stored here (no network at all) is not in flight.
        if (_inFlight is not { IsCompleted: false } && (_askedAt is not { } at || now - at >= (long)NetworkRules.PublicAddressLifetime.TotalMilliseconds || now < at))
        {
            _askedAt = now;
            _inFlight = FetchAsync();
        }

        return _address ?? (_failed ? "Unavailable" : "Checking…");
    }

    private static async Task FetchAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await WebFetch.GetAsync(Service, "text/plain", timeout.Token).ConfigureAwait(true);

            if (response.Status == FetchStatus.Ok && NetworkRules.PublicAddress(response.Body) is { } address)
            {
                _address = address;
                _failed = false;
                return;
            }

            // A wrong body is kept out of the log: it is somebody else's text (a captive portal).
            Log.Info(Scope, $"public address lookup failed: {response.Status} {response.Detail}");
            _failed = _address is null;
        }
        catch (OperationCanceledException)
        {
            Log.Info(Scope, "public address lookup timed out");
            _failed = _address is null;
        }
        finally
        {
            _inFlight = null;
        }
    }
}
