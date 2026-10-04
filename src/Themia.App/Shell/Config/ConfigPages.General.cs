using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The General page: start with Windows, edit mode, and where the data lives.</summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildGeneral(AppHost host)
    {
        var rows = new List<FrameworkElement> { FluentLayout.Header("System") };

        AddStartupRows(rows, host);

        // [S-930] / [S-920] The two desktop-integration switches, as in the original's General page.
        rows.Add(DesktopIconsRow(host));
        rows.Add(GameModeRow(host));

        var editToggle = FluentLayout.Toggle(host.EditMode, host.SetEditMode);

        // Listening only while on screen: the window builds this page again on every visit, and
        // search builds it off-screen, so a subscription made here for good would pile up.
        void OnEditMode(bool editing) => editToggle.IsOn = editing;
        editToggle.Loaded += (_, _) => host.EditModeChanged += OnEditMode;
        editToggle.Unloaded += (_, _) => host.EditModeChanged -= OnEditMode;

        rows.Add(FluentLayout.Row(Glyphs.Edit, "Edit layout",
            "Show the drag handles and the snap grid on the desktop", editToggle));

        // [S-847] A display Themia has not seen before shows this view instead of an empty one of its own.
        var newDisplayOptions = new List<(string Value, string Label)> { (string.Empty, "A new view of its own") };
        newDisplayOptions.AddRange(host.State.Views.Select(v => (v.Id, v.Name)));
        var newDisplayView = new ComboBox { MinWidth = 220 };
        foreach (var (value, label) in newDisplayOptions)
        {
            newDisplayView.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        newDisplayView.SelectedIndex = Math.Max(0, newDisplayOptions.FindIndex(o => o.Value == (host.State.DefaultViewForNewDisplays ?? string.Empty)));
        newDisplayView.SelectionChanged += (_, _) =>
        {
            if (newDisplayView.SelectedItem is ComboBoxItem { Tag: string id })
            {
                host.Mutate(s => s.DefaultViewForNewDisplays = string.IsNullOrEmpty(id) ? null : id, relayout: false);
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Display, "New displays show",
            "The view a monitor shows the first time it is connected", newDisplayView, "monitor", "default view"));

        // [S-1301]/[S-965] The network guard's one switch, as in the original: off, a feed, a
        // favicon, a pinned page or an account server may not make Themia reach loopback, the LAN
        // or a cloud metadata address.
        rows.Add(FluentLayout.Row(Glyphs.Network, "Local network access",
            "Let feeds, web elements and accounts reach addresses on this computer and your local network, such as a self-hosted feed, a mail or calendar server on a NAS, or a router page. Off blocks them, which protects against pages that try to reach your network",
            FluentLayout.Toggle(host.State.Preferences.AllowPrivateNetwork,
                value => host.Mutate(s => s.Preferences.AllowPrivateNetwork = value, relayout: false)),
            "private network", "LAN", "SSRF"));

        rows.Add(FluentLayout.Header("Data"));

        var openFolder = new Button { Content = "Open data folder" };
        openFolder.Click += (_, _) => OpenDataFolder();

        rows.Add(FluentLayout.Row(Glyphs.Info, "Settings",
            "Where the layout and settings live", openFolder));

        // [S-990] Kept last, as in the original. Developer mode adds the Logs page to the menu and,
        // with it, the verbose switch; leaving it turns verbose logging off too ([S-982]).
        rows.Add(FluentLayout.Header("Developer"));
        rows.Add(FluentLayout.Row(Glyphs.Code, "Developer mode",
            "Adds the Logs page with verbose logging, for troubleshooting. What it unlocks is not meant for everyday use",
            FluentLayout.Toggle(host.State.Preferences.DevMode, value => host.Mutate(s =>
            {
                s.Preferences.DevMode = value;
                if (!value)
                {
                    s.Preferences.VerboseLogging = false;
                }
            }, relayout: false)),
            "developer", "dev mode", "logs", "debug"));

        return Page("General", rows);
    }

    private static void OpenDataFolder()
    {
        var folder = Path.GetDirectoryName(Themia.Core.Storage.AppStateStore.DefaultPath());
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not open the data folder", ex);
        }
    }
}
