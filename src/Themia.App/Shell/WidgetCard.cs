using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Windows.Foundation;

namespace Themia.App.Shell;

/// <summary>What a card needs from the surface it lives on.</summary>
internal interface IWidgetCardHost
{
    ScreenMetrics Metrics { get; }

    DesignState Design { get; }

    Ticker Ticker { get; }

    bool EditMode { get; }

    /// <summary>The card moved or resized and is still being dragged: reposition it, do not re-layout everything.</summary>
    void OnGeometryPreview(WidgetCard card);

    /// <summary>The drag ended: persist.</summary>
    void OnGeometryCommitted(WidgetCard card);

    void RequestConfigure(WidgetCard card);

    void RequestDuplicate(WidgetCard card);

    void RequestDelete(WidgetCard card);

    void RequestEditMode(bool enabled);

    bool IsSelected(WidgetCard card);

    void RequestSelect(WidgetCard card);

    void RequestCopy(WidgetCard card);

    void RequestCut(WidgetCard card);

    void RequestPaste();

    bool CanPaste { get; }

    /// <summary>A widget changed its own config: persist it without re-running layout.</summary>
    void RequestSaveConfig();

    /// <summary>
    /// A widget wants the keyboard, or is done with it. The surface is normally unfocusable, so this
    /// is what makes typing into a widget reach it at all.
    /// </summary>
    void RequestTextInput(WidgetCard card, bool wanted);
}

/// <summary>
/// One widget on the desktop: the Fluent card chrome, the drag and resize affordances, and the
/// right-click menu. The widget's own view is dropped inside and knows nothing about input.
/// </summary>
internal sealed partial class WidgetCard : Grid, IWidgetSurface
{
    private const string Scope = "card";
    private const double HandleSize = 10;

    private readonly IWidgetCardHost _host;
    private readonly Border _fill;
    private readonly Border _tint;
    private readonly Border _surface;
    private readonly Border _editOutline;
    private readonly Grid _handles;
    private readonly FrameworkElement _view;

    private FracRect _dragStartRect;
    private Point _dragStartPoint;
    private ResizeHandle? _activeHandle;
    private bool _dragging;

    public WidgetCard(IWidgetCardHost host, WidgetInstance model)
    {
        _host = host;
        Model = model;

        _view = WidgetCatalog.CreateView(model.Kind);

        // Bound before Attach, so a view that reads its label or saves during Attach already has
        // somewhere to read and write.
        if (_view is IWidgetSurfaceAware aware)
        {
            aware.Bind(this);
        }

        if (_view is IWidgetView widgetView)
        {
            widgetView.Attach(host.Ticker, model.Config);
        }

        // Fill and content are separate borders so the surface opacity setting fades the card
        // without fading the text on it.
        _fill = new Border();

        // Windows 11 draws a card as a tint ON something, not as a flat colour. On an opaque
        // window that something is Mica; here the window is genuinely transparent, so the card
        // paints the opaque surface colour Fluent expects underneath and then lays the real
        // CardBackgroundFillColor overlay on top of it. That overlay is the ~5% lift that makes a
        // Windows card look raised rather than merely filled, and using it alone - as a first
        // attempt did - left the card almost invisible over the wallpaper.
        _tint = new Border
        {
            Background = FluentLayout.Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            IsHitTestVisible = false,
        };

        // No padding: the content area goes edge to edge and each view insets its own parts. A card
        // that padded everything pushed every list row away from the edge and stopped its hover
        // highlight short of it, so a row read as a box inside a box rather than as a row. See
        // WidgetLayout.
        _surface = new Border
        {
            Child = _view,
            Padding = new Thickness(0),
        };

        _editOutline = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            Background = null,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _handles = BuildHandles();

        Children.Add(_fill);
        Children.Add(_tint);
        Children.Add(_surface);
        Children.Add(_editOutline);
        Children.Add(_handles);

        Background = new SolidColorBrush(Colors.Transparent); // makes the whole card hit-testable for drags

        // Focusable so that clicking a widget gives the surface keyboard focus. Without something
        // focusable in the tree no KeyDown is raised at all, and the edit-mode shortcuts never fire.
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        ApplyDesign();
        ApplyEditMode(host.EditMode);

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        RightTapped += OnRightTapped;
        DoubleTapped += OnDoubleTapped;
    }

    public WidgetInstance Model { get; }

    /// <summary>The rect the card is currently showing, which during a drag leads the persisted model.</summary>
    public FracRect LiveRect { get; private set; }

    // -----------------------------------------------------------------------------------------
    // Appearance
    // -----------------------------------------------------------------------------------------

    public void ApplyDesign()
    {
        var design = _host.Design;
        var radius = new CornerRadius(design.CornerRadiusPx);

        _fill.CornerRadius = radius;
        _tint.CornerRadius = radius;

        // The content border is rounded too, so anything drawn edge to edge inside it - a meter's
        // graph reaches the bottom and both sides - is clipped to the card's corners instead of
        // squaring them off.
        _surface.CornerRadius = radius;
        _editOutline.CornerRadius = radius;

        // The card paints its OWN base, rather than relying on a window background.
        //
        // Fluent's CardBackgroundFillColor is a ~5% white overlay in dark theme: it is designed to
        // sit on top of an opaque window, not to be the only fill. The widget surface is genuinely
        // transparent, so using it alone made the card invisible and left the clock floating
        // unreadably over whatever was behind the window. SolidBackgroundFillColorBase is the
        // opaque surface colour that overlay is meant to sit on, so it is the correct base here.
        var opacity = Math.Clamp(design.SurfaceOpacity, 0.2, 1.0);

        switch (Model.Surface)
        {
            case WidgetSurface.Transparent:
                // [S-32] No fill and no border: the content sits straight on the wallpaper.
                _fill.Background = null;
                _fill.BorderBrush = null;
                _fill.BorderThickness = new Thickness(0);
                _fill.Opacity = 1;
                _tint.Opacity = 0;
                break;

            case WidgetSurface.Translucent:
                _fill.Background = FluentLayout.Themed("SolidBackgroundFillColorBaseBrush", Colors.Black);
                _fill.BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray);
                _fill.BorderThickness = new Thickness(1);
                _fill.Opacity = Math.Clamp(opacity * 0.6, 0.15, 1.0);
                _tint.Opacity = _fill.Opacity;
                break;

            case WidgetSurface.Card:
            default:
                _fill.Background = FluentLayout.Themed("SolidBackgroundFillColorBaseBrush", Colors.Black);
                _fill.BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray);
                _fill.BorderThickness = new Thickness(1);
                _fill.Opacity = opacity;
                _tint.Opacity = opacity;
                break;
        }
    }

    /// <summary>
    /// Show whether this card is the one the keyboard commands will act on. Without it, Ctrl+C in a
    /// view with several widgets is a guess.
    /// </summary>
    public void ApplySelection()
    {
        var selected = _host.IsSelected(this);
        _editOutline.BorderThickness = new Thickness(selected ? 2 : 1);
        _editOutline.Opacity = selected ? 1.0 : 0.55;
        _handles.Opacity = selected ? 1.0 : 0.55;
    }

    public void ApplyEditMode(bool editing)
    {
        _editOutline.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _handles.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ApplySelection();

        // Outside edit mode the card must not eat pointer input at all: a clock is something you
        // look at, and the desktop underneath stays usable.
        IsHitTestVisible = true;
        ProtectedCursor = editing
            ? InputSystemCursor.Create(InputSystemCursorShape.SizeAll)
            : InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    public void ApplyConfig()
    {
        if (_view is IWidgetView widgetView)
        {
            widgetView.ApplyConfig(Model.Config);
        }

        ApplyDesign();
    }

    public void Detach()
    {
        if (_view is IWidgetView widgetView)
        {
            widgetView.Detach();
        }
    }

    public void SetLiveRect(FracRect rect) => LiveRect = rect;

    /// <summary>
    /// Inset the painted surface inside the cell by half the gap.
    /// <para>
    /// Applied to the fill and the content only. The edit outline and the handles stay on the cell
    /// bounds, which is why they sit slightly outside the widget's visible border — they mark the
    /// space the widget actually owns, and that is the thing being dragged.
    /// </para>
    /// </summary>
    public void ApplyGap(double gapPx)
    {
        var half = double.IsFinite(gapPx) && gapPx > 0 ? gapPx / 2 : 0;
        var inset = new Thickness(half);

        _fill.Margin = inset;
        _surface.Margin = inset;
    }

    // -----------------------------------------------------------------------------------------
    // Handles
    // -----------------------------------------------------------------------------------------

    private Grid BuildHandles()
    {
        // A 3x3 grid places all eight handles without any arithmetic: the outer tracks are handle
        // sized, the centre track takes the rest, and a negative margin straddles each edge.
        var grid = new Grid
        {
            Visibility = Visibility.Collapsed,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Add(grid, ResizeHandle.TopLeft, 0, 0, InputSystemCursorShape.SizeNorthwestSoutheast);
        Add(grid, ResizeHandle.Top, 0, 1, InputSystemCursorShape.SizeNorthSouth);
        Add(grid, ResizeHandle.TopRight, 0, 2, InputSystemCursorShape.SizeNortheastSouthwest);
        Add(grid, ResizeHandle.Left, 1, 0, InputSystemCursorShape.SizeWestEast);
        Add(grid, ResizeHandle.Right, 1, 2, InputSystemCursorShape.SizeWestEast);
        Add(grid, ResizeHandle.BottomLeft, 2, 0, InputSystemCursorShape.SizeNortheastSouthwest);
        Add(grid, ResizeHandle.Bottom, 2, 1, InputSystemCursorShape.SizeNorthSouth);
        Add(grid, ResizeHandle.BottomRight, 2, 2, InputSystemCursorShape.SizeNorthwestSoutheast);

        return grid;

        void Add(Grid parent, ResizeHandle handle, int row, int column, InputSystemCursorShape cursor)
        {
            var dot = new ResizeHandleDot
            {
                Width = HandleSize,
                Height = HandleSize,
                CornerRadius = new CornerRadius(HandleSize / 2),
                Background = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
                BorderBrush = FluentLayout.Themed("SolidBackgroundFillColorBaseBrush", Colors.White),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(
                    column == 0 ? -HandleSize / 2 : 0,
                    row == 0 ? -HandleSize / 2 : 0,
                    column == 2 ? -HandleSize / 2 : 0,
                    row == 2 ? -HandleSize / 2 : 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = handle,
            };

            dot.SetCursorShape(cursor);
            Grid.SetRow(dot, row);
            Grid.SetColumn(dot, column);
            parent.Children.Add(dot);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------------------------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_host.EditMode)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _host.RequestSelect(this);
        Focus(FocusState.Pointer);
        _activeHandle = HitHandle(e.OriginalSource);
        _dragStartRect = LiveRect;
        _dragStartPoint = e.GetCurrentPoint(ParentSurface()).Position;
        _dragging = CapturePointer(e.Pointer);

        e.Handled = _dragging;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var metrics = _host.Metrics;
        if (!metrics.IsUsable)
        {
            return;
        }

        var now = e.GetCurrentPoint(ParentSurface()).Position;
        var dxFrac = (now.X - _dragStartPoint.X) / metrics.Width;
        var dyFrac = (now.Y - _dragStartPoint.Y) / metrics.Height;

        // [S-06] Alt temporarily suspends the snap aid for fine positioning.
        var altHeld = InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        var snap = _host.Design.GridSnap && !altHeld;

        var proposed = _activeHandle is { } handle
            ? GridGeometry.Resize(_dragStartRect, handle, dxFrac, dyFrac, metrics)
            : GridGeometry.Move(_dragStartRect, dxFrac, dyFrac);

        LiveRect = GridGeometry.Settle(proposed, metrics, snap);
        _host.OnGeometryPreview(this);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        ReleasePointerCapture(e.Pointer);
        EndDrag();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Capture can be lost without a release (a display change, another window grabbing input).
        // Committing here rather than dropping the gesture avoids a widget stuck mid-drag.
        if (_dragging)
        {
            EndDrag();
        }
    }

    private void EndDrag()
    {
        _dragging = false;
        _activeHandle = null;
        Model.Rect = LiveRect;
        _host.OnGeometryCommitted(this);
    }

    private ResizeHandle? HitHandle(object? source)
        => source is FrameworkElement { Tag: ResizeHandle handle } ? handle : null;

    private UIElement ParentSurface() => Parent as UIElement ?? this;

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // The tray icon is the documented way in, but a double-click on a widget is the one people
        // actually try — and it is the fallback if the tray icon failed to register.
        _host.RequestEditMode(!_host.EditMode);
        e.Handled = true;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        try
        {
            _host.RequestSelect(this);

            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };

            menu.Items.Add(Item("", "Configure…", () => _host.RequestConfigure(this)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(Item("", "Copy", () => _host.RequestCopy(this), "Ctrl+C"));
            menu.Items.Add(Item("", "Cut", () => _host.RequestCut(this), "Ctrl+X"));

            var paste = Item("", "Paste", _host.RequestPaste, "Ctrl+V");
            paste.IsEnabled = _host.CanPaste;
            menu.Items.Add(paste);

            menu.Items.Add(Item("", "Duplicate", () => _host.RequestDuplicate(this)));
            menu.Items.Add(new MenuFlyoutSeparator());

            menu.Items.Add(Item(
                _host.EditMode ? "" : "",
                _host.EditMode ? "Done editing" : "Edit layout",
                () => _host.RequestEditMode(!_host.EditMode)));

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("", "Delete", () => _host.RequestDelete(this), "Del"));

            menu.ShowAt(this, e.GetPosition(this));
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not open the widget context menu", ex);
        }

        static MenuFlyoutItem Item(string glyph, string text, Action action, string? shortcut = null)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = glyph },
            };

            if (shortcut is not null)
            {
                // Text only: the accelerators are handled on the surface, so registering real
                // KeyboardAccelerators here would double-fire them.
                item.KeyboardAcceleratorTextOverride = shortcut;
            }

            item.Click += (_, _) => action();
            return item;
        }
    }

    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A resize grip.
    /// <para>
    /// A subclass only because <c>UIElement.ProtectedCursor</c> is protected: a resize handle whose
    /// pointer does not change shape reads as decoration rather than something you can grab, and
    /// the shape is the whole affordance. It derives from <see cref="Grid"/> rather than
    /// <c>Rectangle</c> because WinUI seals its shape primitives.
    /// </para>
    /// </summary>
    private sealed partial class ResizeHandleDot : Grid
    {
        public void SetCursorShape(InputSystemCursorShape shape)
            => ProtectedCursor = InputSystemCursor.Create(shape);
    }


    // ---- IWidgetSurface ------------------------------------------------------------------------

    /// <inheritdoc />
    string? IWidgetSurface.Label => Model.Label;

    /// <inheritdoc />
    void IWidgetSurface.RequestSave() => _host.RequestSaveConfig();

    /// <inheritdoc />
    void IWidgetSurface.RequestTextInput(bool wanted) => _host.RequestTextInput(this, wanted);

}
