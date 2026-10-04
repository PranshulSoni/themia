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
internal sealed partial class DesktopWindow : IWidgetCardHost
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
}
