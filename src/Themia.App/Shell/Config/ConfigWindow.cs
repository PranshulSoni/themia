using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Windows.Graphics;

namespace Themia.App.Shell.Config;

/// <summary>
/// The one settings window: app options, views, displays and every widget's own settings, reached
/// through a navigation tree.
/// <para>
/// One window rather than a modal over the desktop surface, matching the original app. A widget's
/// settings are not a transient prompt — you open them, change something, look at the result on the
/// desktop behind, and change something else. A modal blocks exactly that, and a modal hosted by a
/// full-screen click-through surface is worse still: it dims a desktop the user is trying to see.
/// </para>
/// </summary>
internal sealed class ConfigWindow
{
    private const string Scope = "config";

    private readonly AppHost _host;
    private readonly Window _window;
    private readonly NavigationView _nav = new();
    private readonly ScrollViewer _bodyHost = new();

    private nint _hwnd;
    private ConfigTarget _target = new ConfigTarget.App();
    private bool _rebuilding;
    private bool _closed;

    public ConfigWindow(AppHost host)
    {
        _host = host;

        _nav.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        _nav.IsSettingsVisible = false;
        _nav.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        _nav.OpenPaneLength = 260;
        _nav.SelectionChanged += OnSelectionChanged;

        _bodyHost.HorizontalScrollMode = ScrollMode.Disabled;
        _bodyHost.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _bodyHost.Padding = new Thickness(28, 20, 28, 28);
        _nav.Content = _bodyHost;

        _window = new Window
        {
            Title = "Themia settings",
            Content = _nav,
        };

        ConfigureWindow();
        RebuildNavigation();
        RenderBody();

        _host.StateChanged += OnStateChanged;
    }

    /// <summary>Raised once the window has closed, so the host can drop its reference.</summary>
    public event Action? Closed;

    private void ConfigureWindow()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

        // AppWindow sizes are physical pixels, so a fixed size would be a postage stamp on a 4K
        // display at 150%.
        var dpi = Win32.GetDpiForWindow(_hwnd);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        appWindow.Resize(new SizeInt32((int)Math.Round(1040 * scale), (int)Math.Round(760 * scale)));

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "themia.ico");
        if (File.Exists(icon))
        {
            appWindow.SetIcon(icon);
        }

        _window.SystemBackdrop = new MicaBackdrop();
        ApplyTheme();

        _window.Closed += (_, _) =>
        {
            _closed = true;
            _host.StateChanged -= OnStateChanged;
            Closed?.Invoke();
        };
    }

    private void ApplyTheme()
    {
        if (_window.Content is FrameworkElement root)
        {
            root.RequestedTheme = _host.State.Design.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    public void Activate() => _window.Activate();

    public void Close() => _window.Close();

    /// <summary>Show a particular page, bringing the window forward.</summary>
    public void Navigate(ConfigTarget target)
    {
        _target = target;
        _window.Activate();
        Win32.SetForegroundWindow(_hwnd);
        SelectForTarget();
        RenderBody();
    }

    private void OnStateChanged()
    {
        if (_closed)
        {
            return;
        }

        ApplyTheme();
        RebuildNavigation();

        // The thing being edited may have been deleted from the desktop while its page was open.
        if (!TargetStillExists())
        {
            _target = new ConfigTarget.App();
        }

        RenderBody();
    }

    private bool TargetStillExists() => _target switch
    {
        ConfigTarget.Widget widget => _host.Find(widget.WidgetId) is not null,
        ConfigTarget.View view => _host.State.FindView(view.ViewId) is not null,
        ConfigTarget.Display display => _host.Surfaces.Any(s => s.Monitor.DeviceName == display.MonitorId),
        _ => true,
    };

    // -----------------------------------------------------------------------------------------
    // Navigation
    // -----------------------------------------------------------------------------------------

    private void RebuildNavigation()
    {
        // Rebuilding assigns SelectedItem, which raises SelectionChanged; without this guard that
        // would navigate away from the page the caller just asked for.
        _rebuilding = true;
        try
        {
            _nav.MenuItems.Clear();

            _nav.MenuItems.Add(Header("Themia"));
            _nav.MenuItems.Add(Item("General", "", new ConfigTarget.App(AppPage.General)));
            _nav.MenuItems.Add(Item("Appearance", "", new ConfigTarget.App(AppPage.Appearance)));
            _nav.MenuItems.Add(Item("Layout", "", new ConfigTarget.App(AppPage.Layout)));
            _nav.MenuItems.Add(Item("About", "", new ConfigTarget.App(AppPage.About)));

            _nav.MenuItems.Add(new NavigationViewItemSeparator());
            _nav.MenuItems.Add(Header("Views"));

            foreach (var view in _host.State.Views)
            {
                // A view's widgets hang under it, so the tree says where each widget actually lives
                // rather than listing them all flat.
                var node = Item(view.Name, "", new ConfigTarget.View(view.Id));

                foreach (var widget in view.Widgets)
                {
                    var definition = Widgets.WidgetCatalog.Definition(widget.Kind);
                    node.MenuItems.Add(Item(
                        string.IsNullOrWhiteSpace(widget.Label) ? definition.Label : widget.Label,
                        definition.Glyph,
                        new ConfigTarget.Widget(widget.Id)));
                }

                node.IsExpanded = _target is ConfigTarget.Widget w && view.Widgets.Any(x => x.Id == w.WidgetId);
                _nav.MenuItems.Add(node);
            }

            if (_host.Surfaces.Count > 0)
            {
                _nav.MenuItems.Add(new NavigationViewItemSeparator());
                _nav.MenuItems.Add(Header("Displays"));

                foreach (var surface in _host.Surfaces)
                {
                    var name = surface.Monitor.IsPrimary
                        ? $"{surface.Monitor.DeviceName} (primary)"
                        : surface.Monitor.DeviceName;

                    _nav.MenuItems.Add(Item(name, "", new ConfigTarget.Display(surface.Monitor.DeviceName)));
                }
            }

            _nav.FooterMenuItems.Clear();
            _nav.FooterMenuItems.Add(Item("Add a widget", "", new ConfigTarget.Add()));

            SelectForTarget();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private static NavigationViewItemHeader Header(string text) => new() { Content = text };

    private static NavigationViewItem Item(string text, string glyph, ConfigTarget target) => new()
    {
        Content = text,
        Icon = new FontIcon { Glyph = glyph },
        Tag = target,
    };

    private void SelectForTarget()
    {
        foreach (var candidate in Walk(_nav.MenuItems).Concat(Walk(_nav.FooterMenuItems)))
        {
            if (candidate.Tag is ConfigTarget tag && tag == _target)
            {
                _nav.SelectedItem = candidate;
                return;
            }
        }
    }

    private static IEnumerable<NavigationViewItem> Walk(IList<object> items)
    {
        foreach (var item in items)
        {
            if (item is not NavigationViewItem node)
            {
                continue;
            }

            yield return node;

            foreach (var child in Walk(node.MenuItems))
            {
                yield return child;
            }
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_rebuilding || args.SelectedItem is not NavigationViewItem { Tag: ConfigTarget target })
        {
            return;
        }

        _target = target;
        RenderBody();
    }

    // -----------------------------------------------------------------------------------------
    // Body
    // -----------------------------------------------------------------------------------------

    private void RenderBody()
    {
        try
        {
            _bodyHost.Content = ConfigPages.Build(_host, _target, Navigate);
        }
        catch (Exception ex)
        {
            // A page that cannot be built must not take the settings window down with it.
            Log.Error(Scope, $"could not build the page for {_target}", ex);
            _bodyHost.Content = new TextBlock
            {
                Text = "This page could not be shown. See themia.log.",
                Foreground = Application.Current.Resources.TryGetValue("TextFillColorTertiaryBrush", out var b) && b is Brush brush
                    ? brush
                    : new SolidColorBrush(Colors.Gray),
            };
        }
    }
}
