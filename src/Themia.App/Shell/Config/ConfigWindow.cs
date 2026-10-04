using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Settings;

namespace Themia.App.Shell.Config;

/// <summary>
/// The one settings window: app options, views, displays and every widget's own settings, reached
/// through a navigation pane — drawn as Windows 11's Settings app. [S-1540]
/// <para>
/// One window rather than a modal over the desktop surface. A widget's settings are not a transient
/// prompt — you change something, look at the result on the desktop behind, then change something
/// else — and a modal hosted by a full-screen click-through surface dims the very desktop the user
/// is trying to judge.
/// </para>
/// <para>
/// The shape is Settings': Mica behind everything, a tall title bar with the app's mark and name, a
/// back button and the "Find a setting" box in the middle; the navigation pane on the left with
/// its accent selection pill, folding away when the window is narrow; and each page under a large
/// breadcrumb title that stays put while the cards scroll. There is no status bar — Settings has
/// none, and nothing on it was a setting.
/// </para>
/// </summary>
internal sealed partial class ConfigWindow
{
    private const string Scope = "config";

    private readonly AppHost _host;
    private readonly Window _window;
    private readonly NavigationView _nav = new();
    private readonly ScrollViewer _bodyHost = new();
    private readonly Border _pageColumn = new();
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _titleBar = new();
    private readonly NavHistory<ConfigTarget> _history = new();
    private readonly SettingsSearchIndex _index;

    private nint _hwnd;
    private ConfigTarget _target = new ConfigTarget.App();
    private string _structure = string.Empty;
    private bool _rebuilding;
    private bool _closed;

    public ConfigWindow(AppHost host)
    {
        _host = host;
        _index = new SettingsSearchIndex(host);

        _window = new Window
        {
            Title = Str.Settings_Title.Text(),
            Content = BuildChrome(),
        };

        ConfigureWindow();
        UiLanguage.Apply(_window.Content as FrameworkElement);

        _structure = StructureSignature();
        RebuildNavigation();
        _history.Push(_target);
        RenderBody();

        _host.StateChanged += OnStateChanged;
        Themia.Core.Localization.Strings.Changed += OnLanguageChanged;
    }

    /// <summary>Raised once the window has closed, so the host can drop its reference.</summary>
    public event Action? Closed;

    // -----------------------------------------------------------------------------------------
    // Reacting to the desktop
    // -----------------------------------------------------------------------------------------

    private void OnStateChanged()
    {
        if (_closed)
        {
            return;
        }

        ApplyTheme();
        ApplyDevBanner();

        // Only when the tree would actually look different. Rebuilding on every state change meant
        // that dragging a slider tore down and rebuilt the page under the pointer many times a
        // second, which is what made the settings stutter and drop focus mid-drag.
        var structure = StructureSignature();
        if (structure != _structure)
        {
            _structure = structure;
            RebuildNavigation();

            // A page that is gone leaves the back history, and search forgets what it had of it.
            _history.RemoveWhere(t => !Exists(t));
            _index.Invalidate();
            UpdateBackButton();
        }

        // The body is deliberately NOT re-rendered here. It is the thing being edited; the only
        // reason to replace it is that whatever it was editing no longer exists.
        if (!Exists(_target))
        {
            _target = _history.Current ?? new ConfigTarget.App();
            SelectForTarget();
            RenderBody();
        }
    }

    /// <summary>
    /// Everything the navigation tree draws, so a change to any of it rebuilds the tree and a
    /// change to anything else does not.
    /// </summary>
    private string StructureSignature()
    {
        var views = string.Join(
            "|",
            _host.State.Views.Select(v =>
                $"{v.Id}~{v.Name}~{v.Icon}~{string.Join(",", v.Widgets.Select(w => $"{w.Id}={w.Kind}={w.Label}"))}"));

        var displays = string.Join(",", _host.Displays.Select(d => $"{d.Describe()}~{_host.State.DisabledMonitors.Contains(d.Id)}"));
        // [S-990] Developer mode adds and removes the Logs page.
        return $"{views}||{displays}||{_host.State.Preferences.DevMode}";
    }

    private bool Exists(ConfigTarget target) => target switch
    {
        ConfigTarget.Widget widget => _host.Find(widget.WidgetId) is not null,
        ConfigTarget.View view => _host.State.FindView(view.ViewId) is not null,
        ConfigTarget.Display display => _host.FindDisplay(display.MonitorId) is not null,
        ConfigTarget.App app => ConfigPages.AppPages.FirstOrDefault(p => p.Page == app.Page)?.IsAvailable(_host.State) ?? true,
        _ => true,
    };

    // -----------------------------------------------------------------------------------------
    // Body
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Build the page for the current target, put its title in the breadcrumb, and hand its rows to
    /// search — the page on screen is always what search knows of it.
    /// </summary>
    private FrameworkElement RenderBody()
    {
        FrameworkElement page;

        try
        {
            page = ConfigPages.Build(_host, _target, Navigate);
        }
        catch (Exception ex)
        {
            // A page that cannot be built must not take the settings window down with it.
            Log.Error(Scope, $"could not build the page for {_target}", ex);
            page = new SettingsPage(NavTitle(_target)).With([FluentLayout.Note(Str.Shell_Settings_PageFailed.Text())]);
        }

        _pageColumn.Child = page;
        RenderCrumbs(page is SettingsPage settings ? settings.Title : NavTitle(_target));
        _bodyHost.ChangeView(null, 0, null, disableAnimation: true);
        _index.Update(_target, page);
        return page;
    }
}
