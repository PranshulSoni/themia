using Microsoft.UI.Xaml;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Meters_Show.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Signal, Str.WidgetsB_Settings_Network_Connection_Label.Text(),
            Str.WidgetsB_Settings_Network_Connection_Desc.Text(),
            FluentLayout.Toggle(network.ShowConnection, value => { network.ShowConnection = value; save(); }),
            "ssid", "wi-fi", "wifi", "link speed"));

        rows.Add(FluentLayout.Row(Glyphs.Network, Str.WidgetsB_Settings_Network_Address_Label.Text(),
            Str.WidgetsB_Settings_Network_Address_Desc.Text(),
            FluentLayout.Toggle(network.ShowAddress, value => { network.ShowAddress = value; save(); }),
            "ip", "local address"));

        // [S-1194] Says who is asked, because it is a request to somebody else's server.
        rows.Add(FluentLayout.Row(Glyphs.Link, Str.WidgetsB_Settings_Network_PublicAddress_Label.Text(),
            Str.WidgetsB_Settings_Network_PublicAddress_Desc.Text(),
            FluentLayout.Toggle(network.ShowPublicAddress, value => { network.ShowPublicAddress = value; save(); }),
            "public ip", "external ip", "ipify"));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsB_Settings_Network_Session_Label.Text(),
            Str.WidgetsB_Settings_Network_Session_Desc.Text(),
            FluentLayout.Toggle(network.ShowSession, value => { network.ShowSession = value; save(); }),
            "total", "since start"));

        rows.Add(FluentLayout.Row(Glyphs.ListLayout, Str.WidgetsB_Settings_Network_Adapters_Label.Text(),
            Str.WidgetsB_Settings_Network_Adapters_Desc.Text(),
            FluentLayout.Toggle(network.ShowInterfaces, value => { network.ShowInterfaces = value; save(); }),
            "interfaces", "per interface"));

        MeterRows(rows, network, save, Str.WidgetsB_Settings_Network_Refresh.Text());
    }
}
