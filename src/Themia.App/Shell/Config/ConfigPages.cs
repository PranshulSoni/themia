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

/// <summary>
/// The bodies of the config window.
/// <para>
/// Every page edits the live state and saves as it goes, rather than holding a draft behind an OK
/// button: the desktop is visible behind this window, so a setting the user cannot see take effect
/// is a setting they cannot judge. The exception is a widget's own config, which is a clone —
/// see <see cref="BuildWidget"/>.
/// </para>
/// </summary>
internal static partial class ConfigPages
{
    private const string Scope = "config";

    /// <summary>
    /// [S-701] The app pages, in menu order within their slot. A page is in the navigation, and can
    /// be opened, once it has a line here: adding one is this line plus its own
    /// <c>ConfigPages.&lt;Page&gt;.cs</c>, so packages building different pages never edit the same
    /// switch. About stays the last footer entry ([S-493]).
    /// </summary>
    public static IReadOnlyList<AppPageEntry> AppPages { get; } =
    [
        new(AppPage.General, Str.Settings_Nav_General, Glyphs.Settings, NavIcon.Settings, AppPageSlot.Main, BuildGeneral),
        new(AppPage.Appearance, Str.Settings_Section_Appearance, Glyphs.Palette, NavIcon.PaintBrush, AppPageSlot.Main, BuildAppearance),
        new(AppPage.Layout, Str.Shell_Settings_Nav_Layout, Glyphs.Grid, NavIcon.Apps, AppPageSlot.Main, BuildLayout),
        new(AppPage.Accounts, Str.Shell_Settings_Nav_Accounts, Glyphs.Contact, NavIcon.Person, AppPageSlot.Main, _ => AccountsPage.Build()),
        new(AppPage.License, Str.Settings_Nav_License, Glyphs.Certificate, NavIcon.Premium, AppPageSlot.Footer, BuildLicense),
        new(AppPage.Logs, Str.Settings_Nav_Logs, Glyphs.Document, NavIcon.DocumentText, AppPageSlot.Footer, BuildLogs, s => s.Preferences.DevMode),
        new(AppPage.About, Str.Settings_Section_About, Glyphs.Info, NavIcon.QuestionCircle, AppPageSlot.Footer, BuildAbout),
    ];

    public static FrameworkElement Build(AppHost host, ConfigTarget target, Action<ConfigTarget> navigate) => target switch
    {
        ConfigTarget.App app => BuildApp(host, app.Page),
        ConfigTarget.View view => BuildView(host, view.ViewId, navigate),
        ConfigTarget.Display display => BuildDisplay(host, display.MonitorId),
        ConfigTarget.Widget widget => BuildWidget(host, widget.WidgetId, navigate),
        _ => Page(Str.Common_Settings.Text(), []),
    };

    private static FrameworkElement BuildApp(AppHost host, AppPage page)
    {
        foreach (var entry in AppPages)
        {
            if (entry.Page == page)
            {
                return entry.Build(host);
            }
        }

        // A reserved page nobody has built yet: say so in the log rather than throw from the window.
        Log.Warn(Scope, $"no settings page is registered for {page}");
        return Page(Str.Common_Settings.Text(), []);
    }

    // -----------------------------------------------------------------------------------------
    // Shared pieces
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-1541] A page: its rows under the window's breadcrumb title. The title is handed to the
    /// window rather than drawn here, so it stays put while the rows scroll, as in Windows Settings.
    /// </summary>
    private static SettingsPage Page(string title, IReadOnlyList<FrameworkElement> rows, string? subtitle = null)
        => new SettingsPage(title, subtitle).With(rows);

    private static TextBlock Note(string text) => FluentLayout.Note(text);
}
