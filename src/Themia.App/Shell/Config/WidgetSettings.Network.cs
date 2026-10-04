using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    /// <summary>
    /// [S-1195] The network meter's figures. Each says whether a figure may appear; how big the tile
    /// is decides whether it fits.
    /// </summary>
    public static void Network(List<FrameworkElement> rows, NetworkConfig network, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Show"));

        rows.Add(FluentLayout.Row(Glyphs.Signal, "Connection",
            "The adapter in use, its Wi-Fi network and signal, and its link speed",
            FluentLayout.Toggle(network.ShowConnection, value => { network.ShowConnection = value; save(); }),
            "ssid", "wi-fi", "wifi", "link speed"));

        rows.Add(FluentLayout.Row(Glyphs.Network, "IP address",
            "This computer's address on the local network",
            FluentLayout.Toggle(network.ShowAddress, value => { network.ShowAddress = value; save(); }),
            "ip", "local address"));

        // [S-1194] Says who is asked, because it is a request to somebody else's server.
        rows.Add(FluentLayout.Row(Glyphs.Link, "Public IP address",
            "Asks api.ipify.org every 10 minutes, which sees your address",
            FluentLayout.Toggle(network.ShowPublicAddress, value => { network.ShowPublicAddress = value; save(); }),
            "public ip", "external ip", "ipify"));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Session totals",
            "Downloaded and uploaded since Themia started",
            FluentLayout.Toggle(network.ShowSession, value => { network.ShowSession = value; save(); }),
            "total", "since start"));

        rows.Add(FluentLayout.Row(Glyphs.ListLayout, "Adapters",
            "One row per network adapter, busiest first, where the tile has room",
            FluentLayout.Toggle(network.ShowInterfaces, value => { network.ShowInterfaces = value; save(); }),
            "interfaces", "per interface"));

        MeterRows(rows, network, save, "the adapters are read");
    }
}
