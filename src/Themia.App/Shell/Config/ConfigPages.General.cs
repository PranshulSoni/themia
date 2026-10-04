using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The General page: start with Windows, edit mode, and where the data lives.</summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildGeneral(AppHost host)
    {
        var rows = new List<FrameworkElement> { FluentLayout.Header(Str.Shell_Settings_General_System.Text()) };

        rows.Add(LanguageRow(host));

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

        rows.Add(FluentLayout.Row(Glyphs.Edit, Str.Shell_Card_EditLayout.Text(),
            Str.Shell_Settings_General_EditLayoutDesc.Text(), editToggle));

        // [S-847] A display Themia has not seen before shows this view instead of an empty one of its own.
        var newDisplayOptions = new List<(string Value, string Label)> { (string.Empty, Str.Shell_Settings_General_NewDisplayOwnView.Text()) };
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

        rows.Add(FluentLayout.Row(Glyphs.Display, Str.Shell_Settings_General_NewDisplays.Text(),
            Str.Shell_Settings_General_NewDisplaysDesc.Text(), newDisplayView, "monitor", "default view"));

        // [S-1301]/[S-965] The network guard's one switch, as in the original: off, a feed, a
        // favicon, a pinned page or an account server may not make Themia reach loopback, the LAN
        // or a cloud metadata address.
        rows.Add(FluentLayout.Row(Glyphs.Network, Str.Shell_Settings_General_LocalNetwork.Text(),
            Str.Shell_Settings_General_LocalNetworkDesc.Text(),
            FluentLayout.Toggle(host.State.Preferences.AllowPrivateNetwork,
                value => host.Mutate(s => s.Preferences.AllowPrivateNetwork = value, relayout: false)),
            "private network", "LAN", "SSRF"));

        rows.Add(FluentLayout.Header(Str.Settings_Section_Data.Text()));

        var openFolder = new Button { Content = Str.Shell_Settings_General_OpenDataFolder.Text() };
        openFolder.Click += (_, _) => OpenDataFolder();

        rows.Add(FluentLayout.Row(Glyphs.Info, Str.Common_Settings.Text(),
            Str.Shell_Settings_General_DataDesc.Text(), openFolder));

        // [S-990] Kept last, as in the original. Developer mode adds the Logs page to the menu and,
        // with it, the verbose switch; leaving it turns verbose logging off too ([S-982]).
        rows.Add(FluentLayout.Header(Str.Settings_Section_Developer.Text()));
        rows.Add(FluentLayout.Row(Glyphs.Code, Str.Shell_Settings_General_DevMode.Text(),
            Str.Shell_Settings_General_DevModeDesc.Text(),
            FluentLayout.Toggle(host.State.Preferences.DevMode, value => host.Mutate(s =>
            {
                s.Preferences.DevMode = value;
                if (!value)
                {
                    s.Preferences.VerboseLogging = false;
                }
            }, relayout: false)),
            "developer", "dev mode", "logs", "debug"));

        return Page(Str.Settings_Nav_General.Text(), rows);
    }

    /// <summary>
    /// [S-1109] The app's language: Windows' own by default, or one of the nine, switching live.
    /// Each language is listed by its own name ("Deutsch", "日本語"), the same in every language, so
    /// someone who landed in a language they cannot read still finds theirs.
    /// </summary>
    private static Border LanguageRow(AppHost host)
    {
        var options = new List<(AppLanguage Value, string Label)>
        {
            (AppLanguage.System, Str.Settings_Language_System.Text()),
            (AppLanguage.English, Str.Settings_Language_English.Text()),
            (AppLanguage.German, Str.Settings_Language_German.Text()),
            (AppLanguage.Spanish, Str.Settings_Language_Spanish.Text()),
            (AppLanguage.French, Str.Settings_Language_French.Text()),
            (AppLanguage.Portuguese, Str.Settings_Language_Portuguese.Text()),
            (AppLanguage.Italian, Str.Settings_Language_Italian.Text()),
            (AppLanguage.Polish, Str.Settings_Language_Polish.Text()),
            (AppLanguage.Japanese, Str.Settings_Language_Japanese.Text()),
            (AppLanguage.Chinese, Str.Settings_Language_Chinese.Text()),
        };

        return FluentLayout.Row(Glyphs.Language, Str.Settings_Language_Label.Text(), Str.Settings_Language_Desc.Text(),
            FluentLayout.Choice(options, host.State.Preferences.Language, host.SetLanguage),
            "language", "locale", "translation");
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
