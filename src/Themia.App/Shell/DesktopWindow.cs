using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Windows.Foundation;
using Windows.Graphics;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Themia.App.Shell;

/// <summary>
/// The widget surface for one display: a borderless, transparent, never-focused window covering the
/// monitor's work area, holding every card in that monitor's view.
/// <para>
/// One window per monitor rather than one per widget. A widget is a card on a shared pane, so the
/// process pays for one HWND, one composition target and one render thread per display no matter
/// how many widgets the user places.
/// </para>
/// </summary>
internal sealed class DesktopWindow : IWidgetCardHost
{
    private const string Scope = "surface";

    private readonly AppHost _host;
    private readonly Window _window;
    private readonly Canvas _widgetLayer;
    private readonly Path _gridOverlay;
    private readonly Border _toolbar;
    private readonly Grid _root;
    private readonly List<WidgetCard> _cards = [];

    private MonitorTarget _monitor;
    private nint _hwnd;
    private bool _closed;

    public DesktopWindow(AppHost host, MonitorTarget monitor)
    {
        _host = host;
        _monitor = monitor;

        _widgetLayer = new Canvas();

        _gridOverlay = new Path
        {
            Stroke = Brush("ControlStrokeColorDefaultBrush", Colors.Gray),
            StrokeThickness = 1,
            Opacity = 0.35,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _toolbar = BuildToolbar();

        _root = new Grid
        {
            // Transparent, not null: the root still has to receive the pointer input that edit mode
            // needs, while the window itself shows the wallpaper straight through.
            Background = new SolidColorBrush(Colors.Transparent),
            Children = { _gridOverlay, _widgetLayer, _toolbar },
        };

        _window = new Window
        {
            Title = $"Themia ({monitor.DeviceName})",
            Content = _root,
        };

        ConfigureWindow();

        _root.SizeChanged += (_, _) => Relayout();
        _root.Loaded += OnRootLoaded;
        _host.StateChanged += OnStateChanged;
        _host.EditModeChanged += OnEditModeChanged;
    }

    // -----------------------------------------------------------------------------------------
    // IWidgetCardHost
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The DIP-to-physical factor XAML is actually rendering at.
    /// <para>
    /// Read from the XamlRoot rather than assumed from the monitor: <c>GetDpiForMonitor</c> answers
    /// per the calling process's DPI awareness, and at startup it reported 96 for a 144-DPI display,
    /// which would put every widget in the wrong place.
    /// </para>
    /// </summary>
    private double RasterScale
    {
        get
        {
            var scale = _root.XamlRoot?.RasterizationScale ?? 0;
            return scale > 0 ? scale : 1.0;
        }
    }

    /// <summary>
    /// The coordinate space fractional rects resolve into: the window's client area in DIPs.
    /// <para>
    /// Derived from the real client rect divided by the real rasterization scale, <b>not</b> from
    /// <c>ActualWidth</c>. On a scaled display the root's ActualWidth was observed still holding the
    /// pre-DPI size (physical pixels treated as DIPs) because changing scale alone resizes the
    /// content without changing the client's pixel size, so no SizeChanged ever corrected it — and
    /// every widget rendered 1.5x too far right and down, with the far edge off-screen.
    /// </para>
    /// </summary>
    public ScreenMetrics Metrics
    {
        get
        {
            if (Win32.GetClientRect(_hwnd, out var client) && client.Width > 0 && client.Height > 0)
            {
                var scale = RasterScale;
                return new ScreenMetrics(client.Width / scale, client.Height / scale);
            }

            var fallbackScale = RasterScale;
            return new ScreenMetrics(_monitor.WorkArea.Width / fallbackScale, _monitor.WorkArea.Height / fallbackScale);
        }
    }

    public DesignState Design => _host.State.Design;

    public Ticker Ticker => _host.Ticker;

    public bool EditMode => _host.EditMode;

    /// <summary>The display this surface covers.</summary>
    public MonitorTarget Monitor => _monitor;

    public bool IsUnderCursor
    {
        get
        {
            if (!Win32.GetCursorPos(out var cursor))
            {
                return false;
            }

            var area = _monitor.WorkArea;
            return cursor.X >= area.Left && cursor.X < area.Right
                && cursor.Y >= area.Top && cursor.Y < area.Bottom;
        }
    }

    public void OnGeometryPreview(WidgetCard card) => PositionCard(card, card.LiveRect);

    public void OnGeometryCommitted(WidgetCard card)
    {
        PositionCard(card, card.LiveRect);
        _host.SaveGeometryOnly();
    }

    public void RequestConfigure(WidgetCard card) => _ = ShowConfigAsync(card);

    public void RequestDuplicate(WidgetCard card)
    {
        _host.Mutate(state =>
        {
            var view = ViewForThisMonitor(state);
            var copy = card.Model.Clone();

            // Offset the copy so it does not land exactly on top of the original and look like
            // nothing happened.
            var nudged = GridGeometry.Settle(
                copy.Rect with { X = copy.Rect.X + 0.02, Y = copy.Rect.Y + 0.02 },
                Metrics,
                Design.GridSnap);

            copy.Rect = nudged;
            view.Widgets.Add(copy);
        });
    }

    public void RequestDelete(WidgetCard card)
    {
        _host.Mutate(state =>
        {
            var view = ViewForThisMonitor(state);
            view.Widgets.RemoveAll(w => w.Id == card.Model.Id);
        });
    }

    public void RequestEditMode(bool enabled) => _host.SetEditMode(enabled);

    // -----------------------------------------------------------------------------------------
    // Window plumbing
    // -----------------------------------------------------------------------------------------

    private void ConfigureWindow()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = false;
        }

        appWindow.IsShownInSwitchers = false;

        ApplyPlacement(appWindow);

        // A fully transparent backdrop is what lets the wallpaper show between the cards. Without
        // it the window paints its own opaque surface over the whole monitor.
        TryApplyTransparentBackdrop();

        ApplyTheme();
        DesktopLayer.Apply(_hwnd, WidgetLayer.Desktop, interactive: false);
    }

    /// <summary>
    /// Put the window on its monitor's work area.
    /// <para>
    /// <c>AppWindow.MoveAndResize</c> takes <b>physical</b> pixels, which is what
    /// <c>GetMonitorInfo</c> reports, so the work area goes across unconverted. The display scale
    /// only matters inside the window, where XAML lays out in DIPs — see <see cref="Metrics"/>.
    /// </para>
    /// </summary>
    private void ApplyPlacement(AppWindow appWindow)
    {
        var area = _monitor.WorkArea;
        appWindow.MoveAndResize(new RectInt32(area.Left, area.Top, area.Width, area.Height));
    }

    private void TryApplyTransparentBackdrop()
    {
        try
        {
            _window.SystemBackdrop = WidgetBackdrop.Create();
        }
        catch (Exception ex)
        {
            // Without a transparent backdrop the surface would be an opaque sheet over the desktop,
            // which is worse than no widgets at all — so say so loudly rather than ship a black screen.
            Log.Error(Scope, "transparent backdrop unavailable; the widget surface will not be see-through", ex);
        }
    }

    private void ApplyTheme()
    {
        _root.RequestedTheme = Design.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public void ShowSurface()
    {
        // Shown without activation: starting the app must not pull focus away from whatever the
        // user is doing, and the surface belongs behind their windows anyway.
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        AppWindow.GetFromWindowId(windowId).Show(activateWindow: false);

        DesktopLayer.Reassert(_hwnd, WidgetLayer.Desktop, interactive: false);
        Relayout();
    }

    public void CloseSurface()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _host.StateChanged -= OnStateChanged;
        _host.EditModeChanged -= OnEditModeChanged;

        foreach (var card in _cards)
        {
            card.Detach();
        }

        _cards.Clear();
        _window.Close();
    }

    // -----------------------------------------------------------------------------------------
    // Layout
    // -----------------------------------------------------------------------------------------

    private ViewState ViewForThisMonitor(AppState state) => state.ViewForMonitor(_monitor.DeviceName);

    /// <summary>
    /// Once the content is live the XamlRoot exists, so the real scale is finally knowable. Lay out
    /// again here and on every subsequent scale change.
    /// </summary>
    private void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        if (_root.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += (_, _) => Relayout();
        }

        Relayout();
    }

    private void OnStateChanged()
    {
        ApplyTheme();
        Relayout();
    }

    private void OnEditModeChanged(bool editing)
    {
        _toolbar.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _gridOverlay.Visibility = editing && Design.GridSnap ? Visibility.Visible : Visibility.Collapsed;

        foreach (var card in _cards)
        {
            card.ApplyEditMode(editing);
        }

        // Edit mode needs real focus for keyboard input and must float above other windows; normal
        // mode goes back to being part of the desktop.
        DesktopLayer.Apply(_hwnd, WidgetLayer.Desktop, interactive: editing);

        if (editing)
        {
            DesktopLayer.ClearHitRegion(_hwnd);
            Win32.SetForegroundWindow(_hwnd);
            DrawGridOverlay();
        }
        else
        {
            ApplyHitRegion();
        }
    }

    /// <summary>Rebuild the cards for this monitor's view and position them.</summary>
    private void Relayout()
    {
        if (_closed)
        {
            return;
        }

        var view = ViewForThisMonitor(_host.State);
        var wanted = view.Widgets;

        // Reuse the cards that survived the change: rebuilding every card on every state change
        // would restart each widget's tick subscription and flash the whole surface.
        var byId = _cards.ToDictionary(c => c.Model.Id, StringComparer.Ordinal);
        var kept = new List<WidgetCard>(wanted.Count);

        foreach (var model in wanted)
        {
            if (byId.TryGetValue(model.Id, out var existing) && ReferenceEquals(existing.Model, model))
            {
                existing.ApplyConfig();
                kept.Add(existing);
                byId.Remove(model.Id);
                continue;
            }

            var card = new WidgetCard(this, model);
            _widgetLayer.Children.Add(card);
            kept.Add(card);
            byId.Remove(model.Id);
        }

        foreach (var stale in byId.Values)
        {
            stale.Detach();
            _widgetLayer.Children.Remove(stale);
        }

        _cards.Clear();
        _cards.AddRange(kept);

        foreach (var card in _cards)
        {
            card.ApplyEditMode(EditMode);
            PositionCard(card, card.Model.Rect);
        }

        Log.Debug(Scope, $"relayout {_monitor.DeviceName}: view='{view.Name}' widgets={wanted.Count} " +
            $"metrics={Metrics.Width:0}x{Metrics.Height:0} scale={RasterScale:0.00} edit={EditMode}");

        if (EditMode)
        {
            DrawGridOverlay();
        }
        else
        {
            ApplyHitRegion();
        }
    }

    private void PositionCard(WidgetCard card, FracRect rect)
    {
        var metrics = Metrics;
        if (!metrics.IsUsable)
        {
            return;
        }

        card.SetLiveRect(rect);

        var box = GridGeometry.ToPixels(rect, metrics, Design.WidgetGapPx);
        Canvas.SetLeft(card, box.X);
        Canvas.SetTop(card, box.Y);
        card.Width = box.Width;
        card.Height = box.Height;
    }

    /// <summary>
    /// Restrict the window to the widgets themselves, so every click that is not on a widget lands
    /// on the desktop underneath. Without this the surface would be an invisible full-screen pane
    /// that swallows every desktop icon double-click.
    /// </summary>
    private void ApplyHitRegion()
    {
        var metrics = Metrics;
        if (!metrics.IsUsable)
        {
            return;
        }

        // The region is in physical pixels; XAML laid the cards out in DIPs.
        var scale = RasterScale;
        var rects = new List<Win32.Rect>(_cards.Count);

        foreach (var card in _cards)
        {
            var box = GridGeometry.ToPixels(card.Model.Rect, metrics, Design.WidgetGapPx);
            rects.Add(new Win32.Rect
            {
                Left = (int)Math.Floor(box.X * scale),
                Top = (int)Math.Floor(box.Y * scale),
                Right = (int)Math.Ceiling((box.X + box.Width) * scale),
                Bottom = (int)Math.Ceiling((box.Y + box.Height) * scale),
            });
        }

        DesktopLayer.SetHitRegion(_hwnd, rects);
    }

    /// <summary>
    /// [S-06a] The snap grid, drawn only while editing. One <see cref="Path"/> holding every line
    /// rather than a few thousand rectangles: the overlay is a hint, not a scene graph.
    /// </summary>
    private void DrawGridOverlay()
    {
        var metrics = Metrics;
        if (!metrics.IsUsable || !Design.GridSnap)
        {
            _gridOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var (cols, rows) = GridGeometry.GridDims(metrics);
        var group = new GeometryGroup();

        for (var c = 1; c < cols; c++)
        {
            var x = Math.Round(c * metrics.Width / cols) + 0.5;
            group.Children.Add(new LineGeometry { StartPoint = new Point(x, 0), EndPoint = new Point(x, metrics.Height) });
        }

        for (var r = 1; r < rows; r++)
        {
            var y = Math.Round(r * metrics.Height / rows) + 0.5;
            group.Children.Add(new LineGeometry { StartPoint = new Point(0, y), EndPoint = new Point(metrics.Width, y) });
        }

        _gridOverlay.Data = group;
        _gridOverlay.Visibility = Visibility.Visible;
    }

    // -----------------------------------------------------------------------------------------
    // Edit toolbar
    // -----------------------------------------------------------------------------------------

    private Border BuildToolbar()
    {
        var views = new DropDownButton
        {
            Content = "View",
        };

        views.Loaded += (_, _) => views.Flyout = BuildViewFlyout();

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                ToolbarButton("", "Add widget", () => _ = ShowPickerAsync()),
                views,
                ToolbarButton("", "Settings", _host.ShowSettings),
                new AppBarSeparator(),
                AccentButton("", "Done", () => _host.SetEditMode(false)),
            },
        };

        return new Border
        {
            Child = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(8),
            Background = Brush("AcrylicBackgroundFillColorDefaultBrush", Colors.Black),
            BorderBrush = Brush("SurfaceStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
        };
    }

    private MenuFlyout BuildViewFlyout()
    {
        var flyout = new MenuFlyout();
        var activeId = _host.State.ActiveView().Id;

        foreach (var view in _host.State.Views)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = view.Name,
                IsChecked = view.Id == activeId,
            };

            var id = view.Id;
            item.Click += (_, _) => _host.SwitchView(id);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var add = new MenuFlyoutItem { Text = "New view", Icon = new FontIcon { Glyph = "" } };
        add.Click += (_, _) => _host.Mutate(state =>
        {
            var created = ViewState.Create($"View {state.Views.Count + 1}");
            state.Views.Add(created);
            state.ActiveViewId = created.Id;
        });

        flyout.Items.Add(add);
        return flyout;
    }

    private static Button ToolbarButton(string glyph, string text, Action action)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
            },
        };

        button.Click += (_, _) => action();
        ToolTipService.SetToolTip(button, text);
        return button;
    }

    private static Button AccentButton(string glyph, string text, Action action)
    {
        var button = ToolbarButton(glyph, text, action);
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style accent)
        {
            button.Style = accent;
        }

        return button;
    }

    // -----------------------------------------------------------------------------------------
    // Dialogs
    // -----------------------------------------------------------------------------------------

    /// <summary>[S-11] Drop a new widget at the centre of this monitor.</summary>
    public void AddWidgetCentred(WidgetKind kind)
    {
        var definition = WidgetCatalog.Definition(kind);
        var rect = GridGeometry.PlaceAt(
            0.5,
            0.5,
            definition.DefaultWidthPx,
            definition.DefaultHeightPx,
            Metrics,
            Design.GridSnap);

        _host.Mutate(state => ViewForThisMonitor(state).Widgets.Add(WidgetInstance.Create(kind, rect)));
    }

    private async Task ShowPickerAsync()
    {
        try
        {
            var picked = await WidgetPickerDialog.PickAsync(_root.XamlRoot);
            if (picked is { } kind)
            {
                AddWidgetCentred(kind);
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the widget picker failed", ex);
        }
    }

    private async Task ShowConfigAsync(WidgetCard card)
    {
        try
        {
            var changed = await WidgetConfigDialog.EditAsync(_root.XamlRoot, card.Model);
            if (changed)
            {
                _host.Mutate(_ => { });
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the widget config dialog failed", ex);
        }
    }

    private static Brush Brush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
