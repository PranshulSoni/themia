using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.App.Shell.Config;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Widgets;
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
    private readonly Border _placementHint;
    private readonly TextBlock _placementHintText;
    private readonly Canvas _ghostLayer;
    private readonly Grid _ghost;
    private readonly Rectangle _ghostFrame;
    private readonly FontIcon _ghostIcon;
    private readonly TextBlock _ghostLabel;
    private readonly Grid _root;
    private readonly List<WidgetCard> _cards = [];

    /// <summary>
    /// The cards currently asking for the keyboard. A set rather than a flag because two widgets on
    /// one surface can each be typed into, and the last one to finish is the one that gives it back.
    /// </summary>
    private readonly HashSet<WidgetCard> _textInputWanted = [];

    private MonitorTarget _monitor;
    private nint _hwnd;
    private AppWindow? _appWindow;
    private OverlappedPresenter? _presenter;
    private bool _closed;

    public DesktopWindow(AppHost host, MonitorTarget monitor)
    {
        _host = host;
        _monitor = monitor;

        _widgetLayer = new Canvas();

        _gridOverlay = new Path
        {
            // Tertiary text, not a control stroke: the stroke brushes are low-alpha overlays meant
            // for an opaque window, and over a transparent surface they disappear entirely.
            // Kept very faint because the mesh is fine (~10px cells); at a heavier alpha this many
            // lines reads as a grey wash over the desktop rather than as a grid.
            Stroke = Brush("TextFillColorTertiaryBrush", Colors.Gray),
            StrokeThickness = 1,
            Opacity = 0.18,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _toolbar = BuildToolbar();

        _placementHintText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _placementHint = BuildPlacementHint(_placementHintText);

        _ghostIcon = new FontIcon { FontSize = 28 };
        _ghostLabel = Fluent.Text(FluentType.BodyStrong);
        _ghostFrame = new Rectangle
        {
            Fill = Brush("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            Stroke = Brush("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            StrokeThickness = 2,
            StrokeDashArray = [4, 3],
        };
        _ghost = BuildGhost(_ghostFrame, _ghostIcon, _ghostLabel);

        // Never hit-testable: the ghost shows where a click will land, so it must not be the
        // thing that catches the click.
        _ghostLayer = new Canvas { IsHitTestVisible = false, Children = { _ghost } };

        _root = new Grid
        {
            // Transparent, not null: the root still has to receive the pointer input that edit mode
            // needs, while the window itself shows the wallpaper straight through.
            Background = new SolidColorBrush(Colors.Transparent),
            Children = { _gridOverlay, _widgetLayer, _ghostLayer, _toolbar, _placementHint },

            // Focusable so edit-mode keyboard commands (copy / cut / paste / delete) land here.
            IsTabStop = true,
        };

        _root.KeyDown += OnKeyDown;

        _root.PointerPressed += OnRootPointerPressed;
        _root.PointerMoved += OnRootPointerMoved;
        _root.PointerExited += OnRootPointerExited;

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
        _host.SelectionChanged += OnSelectionChanged;
        _host.PlacementChanged += OnPlacementChanged;
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

    /// <summary>The id of the view this surface is currently showing.</summary>
    public string CurrentViewId => ViewForThisMonitor(_host.State).Id;

    /// <summary>This display, as the view switcher sees it. Built on first ask.</summary>
    public IWidgetDisplay? Display => _closed ? null : (_display ??= new SurfaceDisplay(_host, this));

    private SurfaceDisplay? _display;

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

    public void OnGeometryPreview(WidgetCard card)
    {
        PositionCard(card, card.LiveRect);

        // [S-78] A resize never leaves its screen, so only a move asks where it would land.
        var leaving = card.IsMoving && _host.PreviewHandOff(this, card.Model, card.LiveRect);
        card.SetLeaving(leaving);
    }

    public void OnGeometryCommitted(WidgetCard card)
    {
        _host.EndHandOffPreview();
        card.SetLeaving(false);

        if (card.IsMoving && TryHandOffToOtherDisplay(card))
        {
            return;
        }

        PositionCard(card, card.LiveRect);
        _host.SaveGeometryOnly();
    }

    /// <summary>
    /// A drag that ends with the pointer over a different display hands the widget to that display's
    /// surface.
    /// <para>
    /// Only meaningful when the two displays show <i>different</i> views: when they show the same
    /// view the widget is already on both, so there is nothing to move and the drag is an ordinary
    /// reposition. The drop point, not the dragged rect, decides the destination — the rect is
    /// clamped to its own screen ([S-09]) and so can never reach across the boundary.
    /// </para>
    /// </summary>
    private bool TryHandOffToOtherDisplay(WidgetCard card)
    {
        if (!Win32.GetCursorPos(out var cursor))
        {
            return false;
        }

        var area = _monitor.WorkArea;
        var insideThisDisplay = cursor.X >= area.Left && cursor.X < area.Right
            && cursor.Y >= area.Top && cursor.Y < area.Bottom;

        if (insideThisDisplay)
        {
            return false;
        }

        var target = _host.SurfaceAt(cursor.X, cursor.Y);
        if (target is null || ReferenceEquals(target, this))
        {
            return false;
        }

        var sourceViewId = CurrentViewId;
        var targetViewId = target.CurrentViewId;
        if (sourceViewId == targetViewId)
        {
            return false;
        }

        var rect = target.RectForDrop(cursor, card.Model.Rect);
        _host.MoveWidgetToView(card.Model.Id, sourceViewId, targetViewId, rect);
        _host.Select(card.Model.Id);
        Log.Info(Scope, $"widget handed to {target.Monitor.DeviceName} (view '{targetViewId}')");
        return true;
    }

    /// <summary>Centre a widget of the given size on a physical screen point of THIS display.</summary>
    public FracRect RectForDrop(Win32.Point cursor, FracRect size)
    {
        var (xFrac, yFrac) = FractionOf(cursor);
        var raw = new FracRect(xFrac - (size.W / 2), yFrac - (size.H / 2), size.W, size.H);
        return GridGeometry.Settle(raw, Metrics, Design.GridSnap);
    }

    /// <summary>Where a physical screen point falls on this display, as a fraction.</summary>
    private (double X, double Y) FractionOf(Win32.Point point)
    {
        var area = _monitor.WorkArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            return (0.5, 0.5);
        }

        return (
            Math.Clamp((point.X - area.Left) / (double)area.Width, 0, 1),
            Math.Clamp((point.Y - area.Top) / (double)area.Height, 0, 1));
    }

    /// <summary>Paste the clipboard onto this display at the cursor.</summary>
    public void PasteAtCursor()
    {
        if (!_host.Clipboard.HasContent)
        {
            return;
        }

        Win32.GetCursorPos(out var cursor);
        var at = FractionOf(cursor);

        var plan = _host.Clipboard.PlanPaste(new FracRect(at.X, at.Y, 0, 0), Metrics, Design.GridSnap);
        if (plan is null)
        {
            return;
        }

        _host.ApplyPaste(plan.Value, CurrentViewId);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!EditMode)
        {
            return;
        }

        var ctrl = InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (args.Key)
        {
            case Windows.System.VirtualKey.C when ctrl:
                _host.CopySelected();
                break;

            case Windows.System.VirtualKey.X when ctrl:
                _host.CutSelected();
                break;

            case Windows.System.VirtualKey.V when ctrl:
                // [S-74] The display under the pointer, not the one holding the keyboard. Those
                // differ exactly when it matters: copy on one screen, point at the other, paste.
                _host.PasteAtCursor();
                break;

            case Windows.System.VirtualKey.Delete:
                _host.DeleteSelected();
                break;

            case Windows.System.VirtualKey.Escape:
                // Escape backs out one step at a time: out of placing first, out of editing second.
                if (_host.PlacingKind is not null)
                {
                    _host.CancelPlacement();
                }
                else
                {
                    _host.SetEditMode(false);
                }

                break;

            default:
                return;
        }

        args.Handled = true;
    }

    public void RequestConfigure(WidgetCard card)
        => _host.ShowConfig(new ConfigTarget.Widget(card.Model.Id));

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

            // Removing a container hands its children to whatever held it rather than taking
            // them with it. Deleting the box is not deleting the things in it, and the
            // alternative is a dozen widgets gone on one click with no undo.
            view.Dissolve(card.Model.Id);
            view.Widgets.RemoveAll(w => w.Id == card.Model.Id);
        });
    }

    public void RequestEditMode(bool enabled) => _host.SetEditMode(enabled);

    /// <inheritdoc />
    public ViewState? ViewForCards => _closed ? null : ViewForThisMonitor(_host.State);

    /// <inheritdoc />
    public void RequestReparent(WidgetCard card, string? containerId)
    {
        ArgumentNullException.ThrowIfNull(card);

        var id = card.Model.Id;

        _host.Mutate(state =>
        {
            var view = ViewForThisMonitor(state);

            // Refused rather than forced: the tree decides what may hold what, and a move it will
            // not accept leaves the widget exactly where it was.
            if (!view.Reparent(id, containerId))
            {
                Log.Warn(Scope, $"refused to move widget '{id}' into '{containerId ?? "the desktop"}'");
            }
        });
    }

    /// <summary>A widget edited its own config. Persist it, but do not re-lay-out: see [S-128].</summary>
    public void RequestSaveConfig() => _host.SaveGeometryOnly();

    /// <summary>
    /// A widget asked for the keyboard, or gave it up.
    /// <para>
    /// The surface carries <c>WS_EX_NOACTIVATE</c> so that clicking a widget never pulls focus off
    /// the user's work — which is right for every widget except the ones you type into, where it
    /// means the caret blinks and the letters go somewhere else. Rather than a second mechanism,
    /// this feeds the same <c>interactive</c> flag edit mode already uses: the surface becomes
    /// activatable for exactly as long as some widget wants typing, and the z-order controller keeps
    /// it on the desktop regardless, so it still never rises above another window.
    /// </para>
    /// </summary>
    public void RequestTextInput(WidgetCard card, bool wanted)
    {
        if (_closed)
        {
            return;
        }

        var changed = wanted ? _textInputWanted.Add(card) : _textInputWanted.Remove(card);
        if (!changed)
        {
            return;
        }

        ApplyInteractivity();

        // Asking is not enough: the surface has to actually be the foreground window before Windows
        // routes a keystroke to it.
        if (wanted && _textInputWanted.Count == 1 && !_host.EditMode)
        {
            Win32.SetForegroundWindow(_hwnd);
        }
    }

    /// <summary>
    /// The one place that decides whether the surface takes focus. Edit mode and a widget being
    /// typed into are two reasons for the same thing, so they share the flag rather than each
    /// setting the style themselves and overwriting each other.
    /// </summary>
    private void ApplyInteractivity()
    {
        DesktopLayer.Apply(_hwnd, interactive: _host.EditMode || _textInputWanted.Count > 0);
        ApplyChromeless();
    }

    public bool IsSelected(WidgetCard card) => _host.SelectedWidgetId == card.Model.Id;

    public void RequestSelect(WidgetCard card) => _host.Select(card.Model.Id);

    public void RequestCopy(WidgetCard card)
    {
        _host.Select(card.Model.Id);
        _host.CopySelected();
    }

    public void RequestCut(WidgetCard card)
    {
        _host.Select(card.Model.Id);
        _host.CutSelected();
    }

    public void RequestPaste() => PasteAtCursor();

    public bool CanPaste => _host.Clipboard.HasContent;

    // -----------------------------------------------------------------------------------------
    // Window plumbing
    // -----------------------------------------------------------------------------------------

    private void ConfigureWindow()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow = appWindow;
        _presenter = appWindow.Presenter as OverlappedPresenter;
        ApplyChromeless();

        appWindow.IsShownInSwitchers = false;
        _ = _appWindow; // retained for placement and future presenter work

        ApplyPlacement(appWindow);

        // A fully transparent backdrop is what lets the wallpaper show between the cards. Without
        // it the window paints its own opaque surface over the whole monitor.
        TryApplyTransparentBackdrop();

        ApplyTheme();

        // Hand the surface to the z-order controller for good: edit mode makes the window
        // activatable, and an activatable window is raised above its neighbours when clicked unless
        // something says otherwise.
        DesktopZOrder.Track(_hwnd);
        DesktopLayer.Apply(_hwnd, interactive: false);
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

    /// <summary>
    /// Strip the border and title bar.
    /// <para>
    /// Re-applied whenever the window's styles are touched, not just at creation: toggling edit mode
    /// changes the extended styles and activation, and the presenter restores its default frame in
    /// response — which is how the surface grew a title bar the moment you started editing.
    /// </para>
    /// </summary>
    private void ApplyChromeless()
    {
        if (_presenter is not null)
        {
            _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            _presenter.IsResizable = false;
            _presenter.IsMinimizable = false;
            _presenter.IsMaximizable = false;
            _presenter.IsAlwaysOnTop = false;
        }

        // The presenter alone leaves WS_CAPTION in place, so finish the job at the Win32 level.
        DesktopLayer.MakeChromeless(_hwnd);
    }

    /// <summary>
    /// Offset from the window's top-left to the client area's top-left, in physical pixels.
    /// <para>
    /// <c>SetWindowRgn</c> works in window coordinates while the widgets are laid out in client
    /// coordinates; without this the mask is shifted by the frame thickness and stops lining up with
    /// the cards it is meant to trace.
    /// </para>
    /// </summary>
    private (int X, int Y) ClientToWindowOffset()
    {
        if (!Win32.GetWindowRect(_hwnd, out var window))
        {
            return (0, 0);
        }

        var origin = default(Win32.Point);
        if (!Win32.ClientToScreen(_hwnd, ref origin))
        {
            return (0, 0);
        }

        return (origin.X - window.Left, origin.Y - window.Top);
    }

    private void TryApplyTransparentBackdrop()
    {
        try
        {
            _window.SystemBackdrop = new TransparentBackdrop();
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

        DesktopZOrder.Reconcile();
        Relayout();
    }

    public void CloseSurface()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        DesktopZOrder.Forget(_hwnd);
        _host.StateChanged -= OnStateChanged;
        _host.EditModeChanged -= OnEditModeChanged;
        _host.SelectionChanged -= OnSelectionChanged;
        _host.PlacementChanged -= OnPlacementChanged;

        foreach (var card in _cards)
        {
            card.Detach();
        }

        _textInputWanted.Clear();

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

    private void OnSelectionChanged()
    {
        foreach (var card in _cards)
        {
            card.ApplySelection();
        }
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

        // Edit mode needs real focus for the keyboard shortcuts; normal mode goes back to being
        // part of the desktop, unless a widget is being typed into.
        ApplyInteractivity();

        if (editing)
        {
            DesktopLayer.ClearHitRegion(_hwnd);
            _root.Focus(FocusState.Programmatic);
            DrawGridOverlay();
        }
        else
        {
            ApplyHitRegion();
        }
    }

    /// <summary>
    /// Hand every container the cards it holds, and take those cards off the grid.
    /// <para>
    /// Containers are handled after the cards exist and before anything is positioned, because a
    /// container needs its children as finished cards and the grid needs to know which children it
    /// is no longer responsible for. The container does not create them: a widget inside a stack is
    /// the same object with the same lifetime as one on the desktop, so nothing has to be
    /// special-cased to keep it ticking.
    /// </para>
    /// </summary>
    private void ArrangeContainers(ViewState view)
    {
        // Cleared first: a widget that has just left a stack would otherwise keep the suppressed
        // header the stack gave it, and nothing would ever give it back.
        foreach (var card in _cards)
        {
            card.SuppressHeader(false);
        }

        foreach (var card in _cards)
        {
            if (card.View is not IWidgetContainerView container)
            {
                continue;
            }

            var owns = WidgetTree.OwnsChildGeometry(WidgetTree.ContainerOf(card.Model.Kind));
            var children = new List<FrameworkElement>();

            foreach (var child in view.ChildrenOf(card.Model.Id))
            {
                var childCard = _cards.Find(c => ReferenceEquals(c.Model, child));
                if (childCard is null)
                {
                    continue;
                }

                // An element has one parent in the visual tree, so a child a container is about to
                // draw has to leave the widget layer first.
                if (owns)
                {
                    _widgetLayer.Children.Remove(childCard);
                }

                // A stack row and a tab already carry the widget's name, so the card must not
                // repeat it. A group does not name its children, so theirs stays.
                childCard.SuppressHeader(owns);

                children.Add(childCard);
            }

            container.SetChildren(children);

            // A group draws behind the widgets it holds, so it has to be behind them in the visual
            // tree: the widget layer paints in child order and a group added after its members
            // would cover them.
            if (!owns && _widgetLayer.Children.Contains(card))
            {
                _widgetLayer.Children.Remove(card);
                _widgetLayer.Children.Insert(0, card);
            }
        }
    }

    /// <summary>Whether a container draws this widget, rather than the grid placing it.</summary>
    private static bool IsArrangedByContainer(ViewState view, WidgetInstance widget)
    {
        var parent = view.Find(widget.ParentId);
        return parent is not null && WidgetTree.OwnsChildGeometry(WidgetTree.ContainerOf(parent.Kind));
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

            // A card that wanted the keyboard and is now gone must not keep the surface focusable
            // forever.
            RequestTextInput(stale, wanted: false);
        }

        _cards.Clear();
        _cards.AddRange(kept);

        ArrangeContainers(view);

        foreach (var card in _cards)
        {
            card.ApplyEditMode(EditMode);
            card.ApplySelection();

            // A widget a stack or a tab strip has taken over is drawn by that container, inside
            // its rect. Positioning it on the grid as well would leave a second copy somewhere
            // else on the desktop.
            if (!IsArrangedByContainer(view, card.Model))
            {
                PositionCard(card, card.Model.Rect);
            }
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

        // The card occupies the whole CELL. The gap is applied inside it, so the edit outline and
        // handles trace the cell rather than the painted surface, and changing the gap does not
        // move or resize anything.
        var cell = GridGeometry.ToPixels(rect, metrics);
        Canvas.SetLeft(card, cell.X);
        Canvas.SetTop(card, cell.Y);
        card.Width = cell.Width;
        card.Height = cell.Height;
        card.ApplyGap(Design.WidgetGapPx);
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

        // The region is in physical pixels relative to the WINDOW; XAML laid the cards out in DIPs
        // relative to the CLIENT area. Convert both.
        var scale = RasterScale;
        var (offsetX, offsetY) = ClientToWindowOffset();
        var rects = new List<Win32.Rect>(_cards.Count);

        foreach (var card in _cards)
        {
            // The mask traces the PAINTED surface, not the cell: the gap is empty desktop, so a
            // click there should reach the desktop rather than an invisible widget edge.
            var box = GridGeometry.Inset(GridGeometry.ToPixels(card.Model.Rect, metrics), Design.WidgetGapPx);
            rects.Add(new Win32.Rect
            {
                Left = offsetX + (int)Math.Floor(box.X * scale),
                Top = offsetY + (int)Math.Floor(box.Y * scale),
                Right = offsetX + (int)Math.Ceiling((box.X + box.Width) * scale),
                Bottom = offsetY + (int)Math.Ceiling((box.Y + box.Height) * scale),
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

        // Refilled every time it opens. Built once, it kept listing the views that existed when the
        // surface was created and ticking the one that was active then.
        var viewMenu = new MenuFlyout();
        viewMenu.Opening += (_, _) => FillViewFlyout(viewMenu);
        views.Flyout = viewMenu;

        var add = new DropDownButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = Glyphs.Add, FontSize = 14 }, new TextBlock { Text = "Add widget" } },
            },
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, "Add widget");
        add.Flyout = BuildAddFlyout();

        // Quitting lives behind a second click, and away from Done. With no tray entry this is the
        // only place it is offered, but it is also the one button on the toolbar that cannot be
        // undone, and Done is the one right next to it that the user presses constantly.
        var more = new DropDownButton { Content = new FontIcon { Glyph = "\uE712", FontSize = 14 } };
        ToolTipService.SetToolTip(more, "More");

        // An icon-only button has no text for a screen reader to read, and none for anything
        // driving the app from outside to find it by either.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, "More");
        more.Loaded += (_, _) => more.Flyout = BuildMoreFlyout();

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                add,
                views,
                ToolbarButton("", "Settings", _host.ShowSettings),
                more,
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

    /// <summary>
    /// The toolbar's overflow. One row today - quitting - which has nowhere else to live now that
    /// there is no tray menu.
    /// </summary>
    private MenuFlyout BuildMoreFlyout()
    {
        var flyout = new MenuFlyout();

#if DEBUG
        var gallery = new MenuFlyoutItem { Text = "Widget gallery", Icon = new FontIcon { Glyph = Glyphs.Apps } };
        gallery.Click += (_, _) => Gallery.WidgetGallery.Open(WidgetKind.Clock);
        flyout.Items.Add(gallery);
        flyout.Items.Add(new MenuFlyoutSeparator());
#endif

        var exit = new MenuFlyoutItem { Text = "Exit Themia", Icon = new FontIcon { Glyph = "\uE7E8" } };
        exit.Click += (_, _) => _host.RequestExit();
        flyout.Items.Add(exit);

        return flyout;
    }

    private void FillViewFlyout(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        // The view THIS display shows, not the app-wide active one: each display has its own.
        var shownId = CurrentViewId;

        foreach (var view in _host.State.Views)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = view.Name,
                IsChecked = view.Id == shownId,
            };

            var id = view.Id;
            item.Click += (_, _) => _host.ShowView(_monitor.DeviceName, id);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var add = new MenuFlyoutItem { Text = "New view", Icon = new FontIcon { Glyph = "" } };
        add.Click += (_, _) => _host.Mutate(state =>
        {
            var created = ViewState.Create($"View {state.Views.Count + 1}");
            state.Views.Add(created);
            state.ShowViewOn(_monitor.DeviceName, created.Id);
        });

        flyout.Items.Add(add);
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

    // -----------------------------------------------------------------------------------------
    // Adding a widget: the picker, the ghost and the click that places it
    // -----------------------------------------------------------------------------------------

    /// <summary>How many columns the picker spreads its sections across.</summary>
    private const int PickerColumns = 4;

    /// <summary>
    /// [S-420] The widget picker: every widget, in sections, laid out in columns.
    /// <para>
    /// A flyout rather than a page in the settings window, because choosing a widget is a moment's
    /// decision made while looking at the desktop, and the settings window covers the desktop. The
    /// columns are balanced by <see cref="ColumnLayout"/>, which keeps the reading order and never
    /// splits a section from its heading.
    /// </para>
    /// </summary>
    private Flyout BuildAddFlyout()
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,

            // The presenter caps itself at 456px by default, sized for a menu of one column. Left at
            // that, the picker showed two and a half of its four columns and clipped the rest.
            FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
            {
                Setters =
                {
                    new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity),
                    new Setter(FrameworkElement.MaxHeightProperty, double.PositiveInfinity),
                },
            },
        };

        var sections = WidgetCatalog.All
            .GroupBy(d => d.Category)
            .OrderBy(g => g.Key)
            .Select(g => (Category: g.Key, Items: g.ToList()))
            .ToList();

        // A heading costs about one row, so it counts as one when balancing the columns.
        var columnOf = ColumnLayout.Partition([.. sections.Select(s => s.Items.Count + 1)], PickerColumns);
        var columnCount = columnOf.Length == 0 ? 0 : columnOf.Max() + 1;

        var grid = new Grid { ColumnSpacing = Fluent.Space4 };
        var columns = new StackPanel[columnCount];

        for (var c = 0; c < columnCount; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            columns[c] = new StackPanel { MinWidth = 176 };
            Grid.SetColumn(columns[c], c);
            grid.Children.Add(columns[c]);
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var column = columns[columnOf[i]];

            var heading = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            heading.Text = WidgetCatalog.Title(sections[i].Category);
            heading.Margin = new Thickness(Fluent.Space3, column.Children.Count == 0 ? 0 : Fluent.Space4, 0, Fluent.Space1);
            column.Children.Add(heading);

            foreach (var definition in sections[i].Items)
            {
                column.Children.Add(PickerItem(definition, flyout));
            }
        }

        flyout.Content = grid;
        return flyout;
    }

    private Button PickerItem(WidgetDefinition definition, Flyout flyout)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space3,
                Children =
                {
                    new FontIcon { Glyph = definition.Glyph, FontSize = 16 },
                    new TextBlock { Text = definition.Label, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brush("SubtleFillColorTransparentBrush", Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(Fluent.Space3, 6, Fluent.Space3, 6),
        };

        ToolTipService.SetToolTip(button, definition.Description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, definition.Label);

        var kind = definition.Kind;
        button.Click += (_, _) =>
        {
            flyout.Hide();
            _host.BeginPlacement(kind);
        };

        return button;
    }

    private static Border BuildPlacementHint(TextBlock text) => new()
    {
        Child = text,
        Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space4, Fluent.Space3),
        Margin = new Thickness(0, 24, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        CornerRadius = new CornerRadius(8),
        Background = Brush("AcrylicBackgroundFillColorDefaultBrush", Colors.Black),
        BorderBrush = Brush("SurfaceStrokeColorDefaultBrush", Colors.Gray),
        BorderThickness = new Thickness(1),

        // Words, not a control: anything clickable here would have to be told apart from a click
        // meant to place the widget underneath it.
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private static Grid BuildGhost(Rectangle frame, FontIcon icon, TextBlock label)
    {
        // The fill is the stroke's colour at low alpha: the same accent, so it reads as one shape
        // rather than a box inside a box.
        var accent = (frame.Stroke as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        frame.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, accent.R, accent.G, accent.B));

        icon.HorizontalAlignment = HorizontalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;

        var content = new StackPanel
        {
            Spacing = Fluent.Space2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, label },
        };

        return new Grid
        {
            Children = { frame, content },
            Visibility = Visibility.Collapsed,
        };
    }

    /// <summary>
    /// [S-423] Show a widget-shaped outline at a rect on this display: where a widget being placed,
    /// or dragged in from another display, will land. It traces the painted card, inside the gap,
    /// because that is what the user will see once it is there.
    /// </summary>
    public void ShowGhost(FracRect rect, WidgetKind kind)
    {
        var metrics = Metrics;
        if (_closed || !metrics.IsUsable)
        {
            return;
        }

        var box = GridGeometry.Inset(GridGeometry.ToPixels(rect, metrics), Design.WidgetGapPx);
        var definition = WidgetCatalog.Definition(kind);

        _ghostIcon.Glyph = definition.Glyph;
        _ghostLabel.Text = definition.Label;
        _ghostFrame.RadiusX = Design.CornerRadiusPx;
        _ghostFrame.RadiusY = Design.CornerRadiusPx;

        Canvas.SetLeft(_ghost, box.X);
        Canvas.SetTop(_ghost, box.Y);
        _ghost.Width = Math.Max(0, box.Width);
        _ghost.Height = Math.Max(0, box.Height);
        _ghost.Visibility = Visibility.Visible;
    }

    public void HideGhost() => _ghost.Visibility = Visibility.Collapsed;

    /// <summary>
    /// [S-421] Enter or leave placing. The cards stop taking input while a widget is being placed,
    /// so a click over an existing widget places the new one there instead of grabbing the old one,
    /// and the toolbar steps aside for a line saying what the click will do.
    /// </summary>
    private void OnPlacementChanged()
    {
        if (_closed)
        {
            return;
        }

        var placing = _host.PlacingKind;
        _widgetLayer.IsHitTestVisible = placing is null;

        if (placing is { } kind)
        {
            _placementHintText.Text =
                $"Click to place {WidgetCatalog.Definition(kind).Label}  ·  Right-click or Esc to cancel";
            _placementHint.Visibility = Visibility.Visible;
            _toolbar.Visibility = Visibility.Collapsed;

            // The picker was opened on one display, so the pointer is already somewhere: show the
            // ghost there at once rather than waiting for the first move.
            if (IsUnderCursor && Win32.GetCursorPos(out var cursor))
            {
                var (x, y) = FractionOf(cursor);
                ShowGhost(PlacementRect(kind, x, y), kind);
            }
            else
            {
                HideGhost();
            }

            return;
        }

        _placementHint.Visibility = Visibility.Collapsed;
        _toolbar.Visibility = EditMode ? Visibility.Visible : Visibility.Collapsed;
        HideGhost();
    }

    /// <summary>[S-11] Centred on the pointer, then snapped and clamped. Alt bypasses the snap, as when dragging.</summary>
    private FracRect PlacementRect(WidgetKind kind, double xFrac, double yFrac)
    {
        var definition = WidgetCatalog.Definition(kind);
        var snap = Design.GridSnap && !AltHeld();
        return GridGeometry.PlaceAt(xFrac, yFrac, definition.DefaultWidthPx, definition.DefaultHeightPx, Metrics, snap);
    }

    /// <summary>Where a point in this surface's own coordinates falls, as a fraction of it.</summary>
    private (double X, double Y) FractionOfDip(Point dip)
    {
        var metrics = Metrics;
        return metrics.IsUsable
            ? (Math.Clamp(dip.X / metrics.Width, 0, 1), Math.Clamp(dip.Y / metrics.Height, 0, 1))
            : (0.5, 0.5);
    }

    private static bool AltHeld() => InputKeyboardSource
        .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_host.PlacingKind is not { } kind)
        {
            return;
        }

        var (x, y) = FractionOfDip(e.GetCurrentPoint(_root).Position);
        ShowGhost(PlacementRect(kind, x, y), kind);
    }

    /// <summary>The pointer went to another display: the ghost goes with it, so it leaves here.</summary>
    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_host.PlacingKind is not null)
        {
            HideGhost();
        }
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!EditMode)
        {
            return;
        }

        var point = e.GetCurrentPoint(_root);

        if (_host.PlacingKind is { } kind)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                // Placed where the click is, not where the last move put the ghost: the two differ
                // whenever the pointer moved and clicked inside one frame.
                var (x, y) = FractionOfDip(point.Position);
                _host.CompletePlacement(this, PlacementRect(kind, x, y));
            }
            else
            {
                _host.CancelPlacement();
            }

            _root.Focus(FocusState.Pointer);
            e.Handled = true;
            return;
        }

        // Clicking empty surface also gives the surface focus, or the shortcuts stop working as soon
        // as the user clicks away from a widget.
        _host.Select(null);
        _root.Focus(FocusState.Pointer);
    }

    private static Brush Brush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
