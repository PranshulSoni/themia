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

    /// <summary>The view these cards belong to, so a card can ask what it may be moved into.</summary>
    ViewState? ViewForCards { get; }

    /// <summary>Put a widget into a container, or back on the desktop with a null container.</summary>
    void RequestReparent(WidgetCard card, string? containerId);

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
    private readonly Grid _headerHost;
    private readonly Grid _header;
    private readonly TextBlock _headerTitle;

    /// <summary>A title the view worked out for itself, beaten only by one the user typed.</summary>
    private string? _viewTitle;

    /// <summary>Set by a container that names this widget itself. Not the user's choice, so not persisted.</summary>
    private bool _headerSuppressed;
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

        // The header belongs to the card, not to the view. One builder means one look, and it is
        // the only way a "put it at the bottom" setting can exist without asking every widget to
        // honour it. The view contributes its trailing element and nothing else.
        _headerTitle = new TextBlock();
        _header = Fluent.Header(
            WidgetCatalog.Definition(model.Kind).Glyph,
            _headerTitle,
            (_view as IWidgetHeaderContent)?.HeaderTrailing);

        _headerHost = new Grid
        {
            Children = { _header },
            Margin = Fluent.HeaderMargin,
        };

        // The view is bound and attached only once the header exists. A view that names itself
        // during Attach - a weather tile knows its place, a folder listing knows its folder -
        // calls straight back into SetHeaderTitle, and doing that before these fields were built
        // threw a NullReferenceException on the UI thread that took the whole surface down.
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
        // Two rows, one of which is always empty: the header sits in whichever one its placement
        // asks for, so switching it costs a row index rather than a rebuild.
        var content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        Grid.SetRow(_view, 1);
        content.Children.Add(_headerHost);
        content.Children.Add(_view);

        _surface = new Border
        {
            Child = content,
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
        ApplyHeader();
        ApplyDesign();
        ApplyEditMode(host.EditMode);

        // The header sheds by height, so it has to be reconsidered when the height changes.
        SizeChanged += (_, _) => ApplyHeader();

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        RightTapped += OnRightTapped;
        DoubleTapped += OnDoubleTapped;
    }

    public WidgetInstance Model { get; }

    /// <summary>The widget's own view, so the surface can ask whether it is a container.</summary>
    public FrameworkElement View => _view;

    /// <summary>The rect the card is currently showing, which during a drag leads the persisted model.</summary>
    public FracRect LiveRect { get; private set; }

    // -----------------------------------------------------------------------------------------
    // Appearance
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Put the header where the widget asks for it, and name it.
    /// <para>
    /// The title is the user's own when they set one, otherwise the catalogue's. Bottom placement
    /// flips the margin as well as the row, or the gap would end up on the wrong side of it.
    /// </para>
    /// </summary>
    public void ApplyHeader()
    {
        // The user's own label, then whatever the view knows about itself, then the catalogue.
        _headerTitle.Text = !string.IsNullOrWhiteSpace(Model.Label) ? Model.Label
            : !string.IsNullOrWhiteSpace(_viewTitle) ? _viewTitle
            : WidgetCatalog.Definition(Model.Kind).Label;

        // Shed it on a tile too short to carry it, here rather than in twenty views: below this
        // the label is taking the room the content needs, and every widget drew the same
        // conclusion separately with a slightly different threshold. A height of zero is a card
        // that has not been measured yet, not a short one.
        var tooShort = ActualHeight > 0 && ActualHeight < Fluent.HeaderHidesBelow;
        var hidden = Model.Header == HeaderPlacement.Hidden || tooShort || _headerSuppressed;

        _headerHost.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;

        if (hidden)
        {
            return;
        }

        var bottom = Model.Header == HeaderPlacement.Bottom;
        Grid.SetRow(_headerHost, bottom ? 2 : 0);

        _headerHost.Margin = bottom
            ? new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, Fluent.Space4)
            : Fluent.HeaderMargin;
    }

    /// <summary>
    /// Hide the header because a container is already naming this widget.
    /// <para>
    /// A stack row and a tab both carry the widget's name, so the card repeating it underneath is
    /// the same word twice in eighty pixels. This is the container's doing rather than the user's,
    /// so it is a flag on the card and never touches the placement they chose — take the widget out
    /// again and its header comes back exactly as they left it.
    /// </para>
    /// </summary>
    public void SuppressHeader(bool suppressed)
    {
        if (_headerSuppressed == suppressed)
        {
            return;
        }

        _headerSuppressed = suppressed;
        ApplyHeader();
    }

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

        // The title and the placement live on the instance, not the config, so a rename or a
        // move to the bottom arrives here too.
        ApplyHeader();
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
        // The gesture is still "in progress" while the host commits it, so the host can tell a move
        // from a resize: only a move may hand the widget to another display.
        Model.Rect = LiveRect;
        _host.OnGeometryCommitted(this);

        _dragging = false;
        _activeHandle = null;
    }

    /// <summary>
    /// True while the card is being moved, as opposed to resized. A resize that happens to end with
    /// the pointer over another screen is still a resize, and must not carry the widget there.
    /// </summary>
    public bool IsMoving => _dragging && _activeHandle is null;

    /// <summary>
    /// [S-78] Fade the card while its drop would land on another display, so the screen it is
    /// leaving says so as clearly as the ghost on the screen it is going to.
    /// </summary>
    public void SetLeaving(bool leaving) => Opacity = leaving ? 0.35 : 1.0;

    private ResizeHandle? HitHandle(object? source)
        => source is FrameworkElement { Tag: ResizeHandle handle } ? handle : null;

    private UIElement ParentSurface() => Parent as UIElement ?? this;

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Ctrl+Alt+W is the documented way in, but a double-click on a widget is the one people
        // actually try — and it is the fallback when another program already owns the hotkey.
        _host.RequestEditMode(!_host.EditMode);
        e.Handled = true;
    }

    /// <summary>
    /// The "Move into" submenu, or null when there is nowhere to move to.
    /// <para>
    /// Built from what the tree will actually accept rather than from every container on the
    /// desktop, so a container that would make a cycle or nest too deep is simply not offered. An
    /// option that refuses itself when clicked is worse than no option.
    /// </para>
    /// </summary>
    private MenuFlyoutSubItem? BuildMoveMenu()
    {
        var view = _host.ViewForCards;
        if (view is null)
        {
            return null;
        }

        var submenu = new MenuFlyoutSubItem { Text = "Move into" };

        if (Model.ParentId is not null)
        {
            var out_ = new MenuFlyoutItem { Text = "The desktop" };
            out_.Click += (_, _) => _host.RequestReparent(this, null);
            submenu.Items.Add(out_);
            submenu.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var candidate in view.Widgets)
        {
            if (!WidgetTree.IsContainer(candidate.Kind) || !view.CanReparent(Model.Id, candidate.Id))
            {
                continue;
            }

            if (string.Equals(candidate.Id, Model.ParentId, StringComparison.Ordinal))
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(candidate.Label)
                ? WidgetCatalog.Definition(candidate.Kind).Label
                : candidate.Label;

            var id = candidate.Id;
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => _host.RequestReparent(this, id);
            submenu.Items.Add(item);
        }

        return submenu.Items.Count > 0 ? submenu : null;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        try
        {
            _host.RequestSelect(this);

            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };

            menu.Items.Add(Item("", "Configure…", () => _host.RequestConfigure(this)));
            menu.Items.Add(new MenuFlyoutSeparator());

            var move = BuildMoveMenu();
            if (move is not null)
            {
                menu.Items.Add(move);
                menu.Items.Add(new MenuFlyoutSeparator());
            }

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

    /// <inheritdoc />
    void IWidgetSurface.SetHeaderTitle(string? title)
    {
        if (string.Equals(_viewTitle, title, StringComparison.Ordinal))
        {
            return;
        }

        _viewTitle = title;
        ApplyHeader();
    }

}
