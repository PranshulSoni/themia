using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>The Wi-Fi network an adapter is joined to, and how strong its signal is.</summary>
/// <param name="Ssid">The network's name, or null when hidden or not readable.</param>
/// <param name="Signal">Signal quality 0..100, or null.</param>
/// <param name="Refused">[S-1611] Windows refused the query for want of location permission.</param>
internal readonly record struct WlanConnection(string? Ssid, int? Signal, bool Refused = false);

/// <summary>
/// [S-1193] The Wi-Fi network name and signal of a wireless adapter, from the native Wi-Fi API
/// (<c>WlanQueryInterface</c>, <c>wlan_intf_opcode_current_connection</c>).
/// <para>
/// Windows treats the network name as location data: without the user's location permission for
/// desktop apps the call is refused (ERROR_ACCESS_DENIED), and the tile then shows the link speed
/// without a name rather than an error — packaged, with a hint to allow it ([S-1613]). Asked at most
/// every half minute per adapter, and only for an adapter a tile is describing; a change of the
/// location permission ([S-1611]) asks again at once.
/// </para>
/// </summary>
internal static unsafe partial class WlanInfo
{
    private const string Scope = "metrics";

    private const uint ClientVersion = 2;
    private const uint Success = 0;
    private const uint AccessDenied = 5;
    private const int OpcodeCurrentConnection = 7;
    private const int Connected = 1;

    /// <summary>WLAN_CONNECTION_ATTRIBUTES: state, mode, profile name (256 WCHAR), then the association.</summary>
    private const int AssociationOffset = 4 + 4 + (256 * 2);

    /// <summary>The association's SSID length, SSID bytes, then BSS type, BSSID (6), PHY type, PHY index, signal.</summary>
    private const int SsidLengthOffset = AssociationOffset;
    private const int SsidOffset = AssociationOffset + 4;
    private const int SignalOffset = AssociationOffset + 4 + 32 + 4 + 8 + 4 + 4;
    private const int AttributesSize = SignalOffset + 4 + 4 + 4;

    private const long ReuseMs = 30_000;

    private static readonly Dictionary<Guid, (long At, int Generation, WlanConnection? Value)> Cache = [];
    private static uint _lastError;

    /// <summary>The connection of the adapter with this id (<c>NetworkInterface.Id</c>, a GUID), or null.</summary>
    public static WlanConnection? Current(string interfaceId)
    {
        if (!Guid.TryParse(interfaceId, out var guid))
        {
            return null;
        }

        var now = Environment.TickCount64;
        var generation = LocationConsent.Generation;

        if (Cache.TryGetValue(guid, out var cached) && cached.Generation == generation && now - cached.At >= 0 && now - cached.At < ReuseMs)
        {
            return cached.Value;
        }

        WlanConnection? value;

        try
        {
            value = Query(guid);
        }
        catch (DllNotFoundException ex)
        {
            // A Windows edition without the WLAN service (Server, some N SKUs): no names, ever.
            Log.Warn(Scope, "the Wi-Fi API is not installed", ex);
            value = null;
        }

        Cache[guid] = (now, generation, value);
        return value;
    }

    private static WlanConnection? Query(Guid guid)
    {
        nint client = 0;
        uint negotiated = 0;
        var opened = WlanOpenHandle(ClientVersion, 0, &negotiated, &client);

        if (opened != Success)
        {
            Note(opened, "could not open the Wi-Fi API");
            return null;
        }

        nint data = 0;

        try
        {
            uint size = 0;
            var valueType = 0;
            var result = WlanQueryInterface(client, &guid, OpcodeCurrentConnection, 0, &size, &data, &valueType);

            if (result != Success || data == 0 || size < AttributesSize)
            {
                // ERROR_INVALID_STATE is "not connected"; ERROR_ACCESS_DENIED is no location permission.
                Note(result, "could not read the Wi-Fi connection");
                return result == AccessDenied ? new WlanConnection(null, null, Refused: true) : null;
            }

            Note(Success, null);
            var bytes = new ReadOnlySpan<byte>((void*)data, (int)size);

            if (BitConverter.ToInt32(bytes) != Connected)
            {
                return null;
            }

            var length = BitConverter.ToUInt32(bytes[SsidLengthOffset..]);
            var ssid = length <= 32 ? NetworkRules.Ssid(bytes.Slice(SsidOffset, (int)length)) : null;
            var signal = BitConverter.ToUInt32(bytes[SignalOffset..]);

            return new WlanConnection(ssid, signal <= 100 ? (int)signal : null);
        }
        finally
        {
            if (data != 0)
            {
                WlanFreeMemory(data);
            }

            _ = WlanCloseHandle(client, 0); // ignore: closing a client handle has no failure worth acting on
        }
    }

    /// <summary>Log a failure once, on the change, not every half minute.</summary>
    private static void Note(uint error, string? what)
    {
        if (error == _lastError)
        {
            return;
        }

        _lastError = error;

        if (what is not null)
        {
            Log.Info(Scope, $"{what}: {error}");
        }
    }

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanOpenHandle(uint clientVersion, nint reserved, uint* negotiatedVersion, nint* clientHandle);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanCloseHandle(nint clientHandle, nint reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(nint clientHandle, Guid* interfaceGuid, int opCode, nint reserved, uint* dataSize, nint* data, int* valueType);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(nint memory);
}
