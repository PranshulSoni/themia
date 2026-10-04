#if DEBUG
using Themia.App.Services;
using Themia.Core.Model;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1619] The network meter's gallery sample: the packaged build's Wi-Fi line with location refused
/// — signal and link speed, no name, and the "Allow location" hint under it — so the hint can be seen
/// at every size from an unpackaged Debug build, on any machine. Throughput stays live.
/// <para>
/// Read only by a network meter that is a preview, so a desktop tile in the same Debug process never
/// shows it. Nothing is asked of Windows: no consent prompt, and the hint's link does nothing in a
/// preview. Debug builds only.
/// </para>
/// </summary>
internal static class NetworkFixture
{
    /// <summary>The Wi-Fi connection preview tiles show, or null for the machine's.</summary>
    public static WlanConnection? Wifi { get; private set; }

    public static void Install(bool populated) => Wifi = populated ? new WlanConnection(null, 72, Refused: true) : null;

    /// <summary>Every detail line on, so the hint is in the ladder with the rest; a 30-second chart, like the battery's.</summary>
    public static NetworkConfig Config() => new() { IntervalSeconds = 1, GraphSpanSeconds = 30, ShowConnection = true, ShowAddress = true, ShowSession = true };
}
#endif
