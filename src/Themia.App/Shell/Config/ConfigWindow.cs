using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.App.Services.Hearth;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Windows.Graphics;

namespace Themia.App.Shell.Config;

/// <summary>
/// The one settings window: app options, views, displays and every widget's own settings, reached
/// through a navigation tree.
/// <para>
/// One window rather than a modal over the desktop surface. A widget's settings are not a transient
/// prompt — you change something, look at the result on the desktop behind, then change something
/// else — and a modal hosted by a full-screen click-through surface dims the very desktop the user
/// is trying to judge.
/// </para>
/// <para>
/// The chrome (drawn title bar, navigation, status line) is the shape hearth and karpo use, so the
/// three apps read as one family.
/// </para>
/// </summary>
internal sealed class ConfigWindow
{
    private const string Scope = "config";

    private readonly AppHost _host;
    private readonly Window _window;
    private readonly NavigationView _nav = new();
    private readonly ScrollViewer _bodyHost = new();
    private readonly Grid _titleBar = new();
    private readonly TextBlock _statusLeft = new();
    private readonly TextBlock _statusRight = new();

    private nint _hwnd;
    private ConfigTarget _target = new ConfigTarget.App();
    private string _structure = string.Empty;
    private bool _rebuilding;
    private bool _closed;

    public ConfigWindow(AppHost host)
    {
        _host = host;

        _nav.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        _nav.IsSettingsVisible = false;
        _nav.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        _nav.IsPaneToggleButtonVisible = false;
        _nav.OpenPaneLength = 280;
        _nav.SelectionChanged += OnSelectionChanged;

        _bodyHost.HorizontalScrollMode = ScrollMode.Disabled;
        _bodyHost.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _bodyHost.Padding = new Thickness(28, 16, 28, 28);
        _nav.Content = _bodyHost;

        _window = new Window
        {
            Title = "Themia settings",
            Content = BuildChrome(),
        };

        ConfigureWindow();

        _structure = StructureSignature();
        RebuildNavigation();
        RenderBody();
        RefreshStatus();

        _host.StateChanged += OnStateChanged;
        _host.EditModeChanged += OnEditModeChanged;
    }

    /// <summary>Raised once the window has closed, so the host can drop its reference.</summary>
    public event Action? Closed;

    // -----------------------------------------------------------------------------------------
    // Chrome
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Title bar, navigation, status line.
    /// <para>
    /// A plain <see cref="Grid"/> handed to <c>SetTitleBar</c>, not the SDK's <c>TitleBar</c>
    /// control: hearth found that control crashes the process at startup and at exit, and this is
    /// the shape that works.
    /// </para>
    /// </summary>
    private FrameworkElement BuildChrome()
    {
        _titleBar.Height = 40;

        var caption = new TextBlock
        {
            Text = "Themia",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };

        if (Resource<Style>("CaptionTextBlockStyle") is { } captionStyle)
        {
            caption.Style = captionStyle;
        }

        _titleBar.Children.Add(caption);

        Grid.SetRow(_titleBar, 0);
        Grid.SetRow(_nav, 1);

        var status = BuildStatusBar();
        Grid.SetRow(status, 2);

        return new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
            Children = { _titleBar, _nav, status },
        };
    }

    private Border BuildStatusBar()
    {
        foreach (var line in new[] { _statusLeft, _statusRight })
        {
            line.VerticalAlignment = VerticalAlignment.Center;
            if (Resource<Style>("CaptionTextBlockStyle") is { } style)
            {
                line.Style = style;
            }
        }

        _statusLeft.TextTrimming = TextTrimming.CharacterEllipsis;
        _statusRight.Opacity = 0.75;

        var icon = new FontIcon
        {
            Glyph = Glyphs.Info,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75,
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(_statusLeft, 1);
        Grid.SetColumn(_statusRight, 2);

        // The middle column takes what is left and is trimmed, so a long status never pushes the
        // version off the end of the window.
        var grid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { icon, _statusLeft, _statusRight },
        };

        return new Border
        {
            Child = grid,
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            Background = Resource<Brush>("LayerFillColorDefaultBrush"),
            BorderBrush = Resource<Brush>("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
    }

    /// <summary>
    /// What the status line says. Never blank: an empty bar reads as an app that has stopped.
    /// </summary>
    private void RefreshStatus()
    {
        var state = _host.State;
        var widgets = state.Views.Sum(v => v.Widgets.Count);
        var displays = _host.Surfaces.Count;

        var parts = new List<string>
        {
            Count(widgets, "widget"),
            Count(state.Views.Count, "view"),
            Count(displays, "display"),
        };

        if (_host.EditMode)
        {
            parts.Add("editing");
        }

        // Mail is worth a word here because it is the one thing that depends on another program
        // running, and "no messages" in the widget is otherwise ambiguous.
        var mail = AppServices.Mail?.Status switch
        {
            HearthStatus.Ready => "hearth connected",
            HearthStatus.NotRunning => "hearth not running",
            HearthStatus.Denied => "hearth access not granted",
            HearthStatus.Connecting => "connecting to hearth",
            HearthStatus.Failed => "hearth unavailable",
            _ => null,
        };

        if (mail is not null)
        {
            parts.Add(mail);
        }

        _statusLeft.Text = string.Join("  ·  ", parts);
        _statusRight.Text = $"Themia {AppInfo.Version}  ·  {AppInfo.Runtime}";

        static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
    }

    private void ConfigureWindow()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

        // AppWindow sizes are physical pixels, so a fixed size would be a postage stamp on a 4K
        // display at 150%.
        var dpi = Win32.GetDpiForWindow(_hwnd);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        appWindow.Resize(new SizeInt32((int)Math.Round(1060 * scale), (int)Math.Round(780 * scale)));

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "themia.ico");
        if (File.Exists(icon))
        {
            appWindow.SetIcon(icon);
        }

        _window.SystemBackdrop = new MicaBackdrop();
        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(_titleBar);

        ApplyTheme();

        _window.Closed += (_, _) =>
        {
            _closed = true;
            _host.StateChanged -= OnStateChanged;
            _host.EditModeChanged -= OnEditModeChanged;
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

    // -----------------------------------------------------------------------------------------
    // Reacting to the desktop
    // -----------------------------------------------------------------------------------------

    private void OnEditModeChanged(bool editing) => RefreshStatus();

    private void OnStateChanged()
    {
        if (_closed)
        {
            return;
        }

        ApplyTheme();
        RefreshStatus();

        // Only when the tree would actually look different. Rebuilding on every state change meant
        // that dragging a slider tore down and rebuilt the page under the pointer many times a
        // second, which is what made the settings stutter and drop focus mid-drag.
        var structure = StructureSignature();
        if (structure != _structure)
        {
            _structure = structure;
            RebuildNavigation();
        }

        // The body is deliberately NOT re-rendered here. It is the thing being edited; the only
        // reason to replace it is that whatever it was editing no longer exists.
        if (!TargetStillExists())
        {
            _target = new ConfigTarget.App();
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
                $"{v.Id}~{v.Name}~{string.Join(",", v.Widgets.Select(w => $"{w.Id}={w.Kind}={w.Label}"))}"));

        var displays = string.Join(",", _host.Surfaces.Select(s => s.Monitor.Describe()));
        return $"{views}||{displays}";
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
            _nav.MenuItems.Add(Item("General", Glyphs.Settings, new ConfigTarget.App(AppPage.General)));
            _nav.MenuItems.Add(Item("Appearance", Glyphs.Palette, new ConfigTarget.App(AppPage.Appearance)));
            _nav.MenuItems.Add(Item("Layout", Glyphs.Grid, new ConfigTarget.App(AppPage.Layout)));
            _nav.MenuItems.Add(Item("About", Glyphs.Info, new ConfigTarget.App(AppPage.About)));

            _nav.MenuItems.Add(new NavigationViewItemSeparator());
            _nav.MenuItems.Add(Header("Views"));

            foreach (var view in _host.State.Views)
            {
                // A view's widgets hang under it, so the tree says where each widget actually lives
                // rather than listing them all flat.
                var node = Item(view.Name, Glyphs.View, new ConfigTarget.View(view.Id));

                foreach (var widget in view.Widgets)
                {
                    var definition = Widgets.WidgetCatalog.Definition(widget.Kind);
                    node.MenuItems.Add(Item(
                        string.IsNullOrWhiteSpace(widget.Label) ? definition.Label : widget.Label!,
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
                    _nav.MenuItems.Add(Item(
                        surface.Monitor.ShortName,
                        Glyphs.Display,
                        new ConfigTarget.Display(surface.Monitor.DeviceName)));
                }
            }

            _nav.FooterMenuItems.Clear();
            _nav.FooterMenuItems.Add(Item("Add a widget", Glyphs.Add, new ConfigTarget.Add()));

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
                Opacity = 0.7,
            };
        }
    }

    private static T? Resource<T>(string key)
        where T : class
        => Application.Current.Resources.TryGetValue(key, out var value) ? value as T : null;
}
