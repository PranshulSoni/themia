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

        var startup = FluentLayout.Toggle(StartupRegistration.IsEnabled(), value =>
        {
            // Reflect what the registry actually ended up at, so a failed write does not leave the
            // toggle claiming something untrue.
            var achieved = StartupRegistration.SetEnabled(value);
            host.Mutate(s => s.Design.RunAtStartup = achieved, relayout: false);
        });

        rows.Add(FluentLayout.Row(Glyphs.Startup, "Start with Windows", "Adds a per-user startup entry", startup));

        var editToggle = FluentLayout.Toggle(host.EditMode, host.SetEditMode);

        // Listening only while on screen: the window builds this page again on every visit, and
        // search builds it off-screen, so a subscription made here for good would pile up.
        void OnEditMode(bool editing) => editToggle.IsOn = editing;
        editToggle.Loaded += (_, _) => host.EditModeChanged += OnEditMode;
        editToggle.Unloaded += (_, _) => host.EditModeChanged -= OnEditMode;

        rows.Add(FluentLayout.Row(Glyphs.Edit, "Edit layout",
            "Show the drag handles and the snap grid on the desktop", editToggle));

        rows.Add(FluentLayout.Header("Data"));

        var openFolder = new Button { Content = "Open data folder" };
        openFolder.Click += (_, _) => OpenDataFolder();

        rows.Add(FluentLayout.Row(Glyphs.Info, "Settings and log",
            "Where the layout, settings and log file live", openFolder));

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
