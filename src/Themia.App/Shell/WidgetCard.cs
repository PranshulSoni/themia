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
using Themia.Core.Widgets;
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
    /// [S-801] A text box in the widget gained or lost focus. The surface is normally unfocusable, so
    /// this — the keyboard lease, held for typing — is what makes typing into a widget reach it at all.
    /// </summary>
    void RequestTextInput(WidgetCard card, bool wanted);

    /// <summary>
    /// [S-800] The user pressed somewhere in this card outside edit mode: the keyboard lease moves to
    /// it when its view takes keys (<see cref="IWidgetKeys"/>), and ends for whichever widget had it
    /// otherwise.
    /// </summary>
    void KeyboardPress(WidgetCard card);

    /// <summary>The display these cards are on, for a widget that switches it; null where there is none.</summary>
    IWidgetDisplay? Display { get; }

    /// <summary>[S-1251] True for the gallery's host: its cards are previews and must not act on the machine.</summary>
    bool IsPreview => false;

    /// <summary>
    /// [S-871]/[S-873] The card's drawn frame changed — collapsed to its bar, hidden or shown — so it
    /// is positioned again and the hit region redone. Nothing is persisted.
    /// </summary>
    void OnCardFrameChanged(WidgetCard card)
    {
        // ignore: a host with no hit region (the gallery) has nothing to redo
    }

    /// <summary>[S-874] Bring the widget to the front of the view, or send it to the back.</summary>
    void RequestZOrder(WidgetCard card, bool front)
    {
        // ignore: a host with no view (the gallery) has no order to change
    }

    /// <summary>[S-866] A container's row (or tab) asked to act on one of its children.</summary>
    void RequestChildAction(WidgetCard container, string childId, ContainerChildAction action)
    {
        // ignore: as above
    }

    /// <summary>[S-866] A container's child was dragged to <paramref name="index"/> among its siblings.</summary>
    void RequestMoveChildTo(WidgetCard container, string childId, int index)
    {
        // ignore: as above
    }

    /// <summary>[S-870] Open the widget picker with <paramref name="container"/> chosen as where the new widget goes.</summary>
    void RequestAddInside(WidgetCard container)
    {
        // ignore: the gallery has no picker
    }
}

/// <summary>
/// One widget on the desktop: the Fluent card chrome, the drag and resize affordances, and the
/// right-click menu. The widget's own view is dropped inside and knows nothing about input.
/// </summary>
internal sealed partial class WidgetCard : Grid, IWidgetSurface, IContainerSurface
{
    private const string Scope = "card";
    private const double HandleSize = 10;

    /// <summary>[S-871] The tallest control a header row holds (a group's collapse button).</summary>
    public const double HeaderControlHeight = 24;

    private readonly IWidgetCardHost _host;
    private readonly Border _fill;
    private readonly Border _tint;
    private readonly Border _surface;

    /// <summary>[S-824] The border, drawn as a shape because a <see cref="Border"/> cannot dash.</summary>
    private readonly Rectangle _stroke;

    /// <summary>
    /// [S-826] Carries the design's font to the content: text inherits a font family from the
    /// nearest presenter or control above it, and a <see cref="Border"/> has none to give.
    /// </summary>
    private readonly ContentPresenter _fontScope;

    /// <summary>[S-821] The text size the text already on this card was laid out at.</summary>
    private double _textScale = FluentType.TextScale;
    private readonly Grid _headerHost;

    /// <summary>[S-862] The header, upright or turned up a side edge.</summary>
    private readonly SidewaysPanel _headerTurn;

    /// <summary>[S-863] The card's surface behind the header bar alone, for a view that paints its own area.</summary>
    private readonly Border _headerBase = new() { IsHitTestVisible = false };

    private readonly Border _headerTint = new() { IsHitTestVisible = false };

    /// <summary>[S-873] The view asked to paint nothing (outside edit mode).</summary>
    private bool _hidden;

    /// <summary>[S-871] Collapsed to the header bar (a group).</summary>
    private bool _collapsed;
    private readonly Grid _header;
    private readonly TextBlock _headerTitle;

    /// <summary>A title the view worked out for itself, beaten only by one the user typed.</summary>
    private string? _viewTitle;

    /// <summary>Set by a container that names this widget itself. Not the user's choice, so not persisted.</summary>
    private bool _headerSuppressed;
    private readonly Border _editOutline;
    private readonly Grid _handles;
    private readonly FrameworkElement _view;

    /// <summary>The header and the view together: what [S-850]'s content scale magnifies.</summary>
    private readonly Grid _content;

    private double _contentScale = 1.0;

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

        // Padding rather than margin, so a header fill ([S-828]) reaches the card's edges.
        _headerHost = new Grid
        {
            Children = { _header },
            Padding = Fluent.HeaderMargin,
        };

        // [S-861] A click on a group's bar (not on its button) toggles it outside edit mode.
        _headerHost.Tapped += OnHeaderTapped;

        _headerTurn = new SidewaysPanel
        {
            Children = { new Grid { Children = { _headerBase, _headerTint, _headerHost } } },
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
        _fill = new Border
        {
            // [S-27c] The opaque base runs under the stroke, not just inside it. Fluent's card stroke
            // is a ~10% overlay meant to sit on something; WinUI's default paints a border's
            // background only inside the stroke, so the outer pixel of every card was a nearly clear
            // ring over the wallpaper. Two neighbours at a 0px gap read as a visible gap between them.
            BackgroundSizing = BackgroundSizing.OuterBorderEdge,
        };

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
        // [S-862] Three columns as well, for a header on the left or right edge.
        var content = new Grid
        {
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

        Grid.SetRow(_view, 1);
        Grid.SetColumn(_view, 1);
        content.Children.Add(_headerTurn);
        content.Children.Add(_view);

        _content = content;
        _fontScope = new ContentPresenter
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        _surface = new Border
        {
            Child = _fontScope,
            Padding = new Thickness(0),
        };

        // Over the content, as a CSS border sits round it: a full-bleed chart would otherwise cover
        // the line along its edges. Never hit-testable, so it cannot take a click from the widget.
        _stroke = new Rectangle { IsHitTestVisible = false };

        _surface.SizeChanged += (_, _) => FitContent();

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
        Children.Add(_stroke);
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

        // [S-663] A view that discloses by size is told its slot - the card less the header - once
        // per layout pass that changed it. The view's own SizeChanged is that signal exactly: it is
        // raised after arrange, only when the slot really changed (a resize, the header shedding or
        // moving), and the slot is a star row of a fixed-size card, so nothing the view shows in
        // response can feed back into it.
        if (_view is IWidgetSizeAware sizeAware)
        {
            _view.SizeChanged += (_, e) =>
            {
                if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
                {
                    sizeAware.ApplySize(new WidgetArea(e.NewSize.Width, e.NewSize.Height));
                }
            };
        }

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        RightTapped += OnRightTapped;
        DoubleTapped += OnDoubleTapped;

        // [S-800] Handled presses too: a click on a row's button or a list item is still a click in
        // this widget, and the controls inside mark their presses handled.
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnLeasePointerPressed), handledEventsToo: true);
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
    /// The title is the user's own when they set one, otherwise the catalogue's. A header on the far
    /// edge (bottom, right) flips the padding as well, or the gap would end up on the wrong side of
    /// it; a side header ([S-862]) is the same header turned to run up the edge.
    /// </para>
    /// </summary>
    public void ApplyHeader()
    {
        // The user's own label, then whatever the view knows about itself, then the catalogue.
        _headerTitle.Text = !string.IsNullOrWhiteSpace(Model.Label) ? Model.Label
            : !string.IsNullOrWhiteSpace(_viewTitle) ? _viewTitle
            : WidgetCatalog.Definition(Model.Kind).Label;

        var placement = HeaderEdge;
        var side = HeaderRules.IsSide(placement);

        // Shed it on a tile too short to carry it, here rather than in twenty views: below this
        // the label is taking the room the content needs, and every widget drew the same
        // conclusion separately with a slightly different threshold. A size of zero is a card
        // that has not been measured yet, not a short one. A collapsed card IS its bar ([S-871]).
        var extent = side ? ActualWidth : ActualHeight;
        var tooShort = !_collapsed && extent > 0 && extent < Fluent.HeaderHidesBelow;
        var hidden = placement == HeaderPlacement.Hidden || tooShort || _headerSuppressed;

        _headerTurn.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;

        if (hidden)
        {
            return;
        }

        var far = placement is HeaderPlacement.Bottom or HeaderPlacement.Right;
        _headerTurn.Sideways = side;
        Grid.SetRow(_headerTurn, placement == HeaderPlacement.Bottom ? 2 : 0);
        Grid.SetRowSpan(_headerTurn, side ? 3 : 1);
        Grid.SetColumn(_headerTurn, placement == HeaderPlacement.Right ? 2 : 0);
        Grid.SetColumnSpan(_headerTurn, side ? 1 : 3);

        _headerHost.Padding = _collapsed ? new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, Fluent.Space2)
            : far ? new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, Fluent.Space4)
            : Fluent.HeaderMargin;

        ApplyHeaderFill();
    }

    /// <summary>
    /// Where the header is drawn: the user's placement, except that a collapsed card always has its
    /// bar — a collapsed group with no header would be nothing to click to open it again.
    /// </summary>
    public HeaderPlacement HeaderEdge => _collapsed && Model.Header == HeaderPlacement.Hidden ? HeaderPlacement.Top : Model.Header;

    /// <summary>[S-871] The header bar's thickness: what a collapsed card shrinks to, and a see-through group's band.</summary>
    public static double BarThickness => (Fluent.Space2 * 2) + Math.Max(FluentType.BodyStrong.LineHeight, HeaderControlHeight);

    /// <summary>[S-871] Whether the card is collapsed to its header bar.</summary>
    public bool IsCollapsedToBar => _collapsed;

    /// <summary>[S-872] What the window's hit region needs from this card.</summary>
    public CardHit HitInfo(bool editMode) => new(
        Shown: !_hidden || editMode,
        BandOnly: !editMode && _view is IWidgetArea { BandOnly: true },
        Header: _headerTurn.Visibility == Visibility.Visible ? HeaderEdge : HeaderPlacement.Hidden,
        Bar: BarThickness);

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

    /// <summary>
    /// Paint the card from the design: corner radius, fill colour and opacity, border, header fill and
    /// font. The one per-card apply path ([S-820]..[S-829]); the app-wide half (theme, accent, text
    /// size) is <see cref="DesignApplier"/>. Runs on every state change, and only sets properties, so
    /// a slider dragged across the Appearance page re-paints cards without rebuilding any.
    /// </summary>
    public void ApplyDesign()
    {
        var design = _host.Design;
        var radiusPx = DesignLook.CornerRadius(design.CornerRadiusPx);
        var radius = new CornerRadius(radiusPx);

        _fill.CornerRadius = radius;
        _tint.CornerRadius = radius;

        // The content border is rounded too, so anything drawn edge to edge inside it - a meter's
        // graph reaches the bottom and both sides - is clipped to the card's corners instead of
        // squaring them off.
        _surface.CornerRadius = radius;
        _editOutline.CornerRadius = radius;

        // [S-823] The card paints its OWN base, rather than relying on a window background.
        //
        // Fluent's CardBackgroundFillColor is a ~5% white overlay in dark theme: it is designed to
        // sit on top of an opaque window, not to be the only fill. The widget surface is genuinely
        // transparent, so using it alone made the card invisible and left the clock floating
        // unreadably over whatever was behind the window. SolidBackgroundFillColorBase is the
        // opaque surface colour that overlay is meant to sit on, so it is the correct base here.
        // A colour of the user's own replaces both: it is meant to be exactly that colour.
        var opacity = DesignLook.FillOpacity(Model.Surface, design.SurfaceOpacity);
        var custom = DesignApplier.UserBrush(design.WidgetBackgroundColor);

        // [S-863] A view that paints its own area (a group) gets the card's surface behind its bar only.
        var ownArea = _view is IWidgetArea;
        _fill.Background = opacity <= 0 || ownArea ? null
            : custom ?? FluentLayout.Themed("SolidBackgroundFillColorBaseBrush", Colors.Black);
        _fill.BorderBrush = null;
        _fill.BorderThickness = new Thickness(0);
        _fill.Opacity = opacity <= 0 ? 1 : opacity;
        _tint.Opacity = custom is null && !ownArea ? opacity : 0;

        // [S-824] The border: style, width, colour and opacity, over the content.
        var border = DesignLook.Border(Model.Surface, design.BorderStyle, design.BorderWidthPx, design.BorderOpacity);
        if (border.Visible && (!_hidden || _host.EditMode))
        {
            _stroke.Visibility = Visibility.Visible;
            _stroke.Stroke = DesignApplier.UserBrush(design.BorderColor)
                ?? FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray);
            _stroke.StrokeThickness = border.Thickness;
            _stroke.Opacity = border.Opacity;

            // A shape strokes inside its bounds along a path inset by half the stroke, so the path's
            // radius is the card's less that half, or the corners would not meet the fill's.
            _stroke.RadiusX = _stroke.RadiusY = Math.Max(0, radiusPx - (border.Thickness / 2));

            var dashes = new DoubleCollection();
            foreach (var dash in border.Dashes)
            {
                dashes.Add(dash);
            }

            _stroke.StrokeDashArray = dashes;
            _stroke.StrokeDashCap = border.RoundCaps ? PenLineCap.Round : PenLineCap.Flat;
            _stroke.StrokeStartLineCap = _stroke.StrokeEndLineCap = _stroke.StrokeDashCap;
        }
        else
        {
            _stroke.Visibility = Visibility.Collapsed;
        }

        ApplyHeaderFill();

        // [S-826] The design's font for everything in the card that does not pick its own; icons
        // name Segoe Fluent Icons themselves, so their glyphs are untouched.
        if (DesignApplier.ContentFont is { } font)
        {
            _fontScope.FontFamily = font;
        }
        else
        {
            _fontScope.ClearValue(ContentPresenter.FontFamilyProperty);
        }
    }

    /// <summary>
    /// [S-828] A header of its own colour; none means it shares the widget's fill. The corners on
    /// the card's edge follow the card's so the strip does not square them — all four when the card
    /// is collapsed to the bar. [S-863] Behind a group's bar the card's own surface, since the group
    /// paints its area itself.
    /// </summary>
    private void ApplyHeaderFill()
    {
        var design = _host.Design;
        var r = DesignLook.CornerRadius(design.CornerRadiusPx);

        // A side header is turned a quarter: its upright top is the left edge, its bottom the right.
        var radius = _collapsed ? new CornerRadius(r)
            : HeaderEdge is HeaderPlacement.Bottom or HeaderPlacement.Right ? new CornerRadius(0, 0, r, r)
            : new CornerRadius(r, r, 0, 0);

        _headerHost.Background = DesignApplier.UserBrush(design.HeaderBackgroundColor, design.HeaderOpacity);
        _headerHost.CornerRadius = radius;

        var ownArea = _view is IWidgetArea;
        var opacity = DesignLook.FillOpacity(Model.Surface, design.SurfaceOpacity);
        var custom = DesignApplier.UserBrush(design.WidgetBackgroundColor);

        _headerBase.Background = ownArea && opacity > 0
            ? custom ?? FluentLayout.Themed("SolidBackgroundFillColorBaseBrush", Colors.Black)
            : null;
        _headerBase.Opacity = opacity;
        _headerTint.Background = ownArea && custom is null && opacity > 0
            ? FluentLayout.Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent)
            : null;
        _headerTint.Opacity = opacity;
        _headerBase.CornerRadius = radius;
        _headerTint.CornerRadius = radius;
    }

    /// <summary>
    /// [S-873] Draw the card or nothing. Hidden, the fill, content and border collapse (the view
    /// stays attached and keeps receiving its updates, so it can show itself again); edit mode
    /// always draws it, faded, so it can be found.
    /// </summary>
    private void ApplyHidden()
    {
        var editing = _host.EditMode;
        var visibility = !_hidden || editing ? Visibility.Visible : Visibility.Collapsed;

        _fill.Visibility = visibility;
        _tint.Visibility = visibility;
        _surface.Visibility = visibility;
        _surface.Opacity = _hidden && editing ? 0.5 : 1;
    }

    /// <summary>
    /// [S-821] Bring text already on the card to the current text size, before the view lays itself
    /// out again from the (already scaled) type ramp. Text a view builds from the ramp is right by
    /// construction; this catches what it built once — labels, the header — so the card follows
    /// the setting without being rebuilt. Only sizes set on the element itself are scaled; text
    /// that inherits its size follows its parent.
    /// </summary>
    private void ApplyTextScale()
    {
        var scale = FluentType.TextScale;
        if (Math.Abs(scale - _textScale) < 0.0001)
        {
            return;
        }

        var ratio = scale / _textScale;
        _textScale = scale;
        Rescale(_content, ratio);
    }

    private static void Rescale(DependencyObject node, double ratio)
    {
        switch (node)
        {
            case TextBlock text when text.ReadLocalValue(TextBlock.FontSizeProperty) != DependencyProperty.UnsetValue:
                text.FontSize *= ratio;
                if (text.LineHeight > 0)
                {
                    text.LineHeight *= ratio;
                }

                break;

            case RichTextBlock rich when rich.ReadLocalValue(RichTextBlock.FontSizeProperty) != DependencyProperty.UnsetValue:
                rich.FontSize *= ratio;
                break;

            case Control control when control.ReadLocalValue(Control.FontSizeProperty) != DependencyProperty.UnsetValue:
                control.FontSize *= ratio;
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Rescale(VisualTreeHelper.GetChild(node, i), ratio);
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
        ApplyHidden();
        (_view as IContainerChrome)?.ApplyEditMode(editing);

        // Outside edit mode the card must not eat pointer input at all: a clock is something you
        // look at, and the desktop underneath stays usable.
        IsHitTestVisible = true;
        ProtectedCursor = editing
            ? InputSystemCursor.Create(InputSystemCursorShape.SizeAll)
            : InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    public void ApplyConfig()
    {
        // Before the view re-lays itself out from the ramp, or its fresh sizes would be scaled twice.
        ApplyTextScale();

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
    /// [S-850] Magnify the card's content — header, text, icons, padding — without touching its cell:
    /// the card stays exactly where it is and the same size, and the view lays out in the smaller
    /// (or larger) logical space the scale leaves it, so it discloses less (or more) per [S-663].
    /// </summary>
    public void ApplyContentScale(double scale)
    {
        var next = double.IsFinite(scale) && scale > 0 ? scale : 1.0;
        if (Math.Abs(next - _contentScale) < 0.0001)
        {
            return;
        }

        _contentScale = next;
        FitContent();
    }

    /// <summary>
    /// WinUI has no layout transform, so the content is laid out at the painted size divided by the
    /// scale and then render-scaled back up to fill it exactly.
    /// </summary>
    private void FitContent()
    {
        if (Math.Abs(_contentScale - 1.0) < 0.0001)
        {
            _content.Width = double.NaN;
            _content.Height = double.NaN;
            _content.HorizontalAlignment = HorizontalAlignment.Stretch;
            _content.VerticalAlignment = VerticalAlignment.Stretch;
            _content.RenderTransform = null;
            return;
        }

        var width = _surface.ActualWidth;
        var height = _surface.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return; // not laid out yet; SizeChanged brings it back here
        }

        _content.HorizontalAlignment = HorizontalAlignment.Left;
        _content.VerticalAlignment = VerticalAlignment.Top;
        _content.Width = width / _contentScale;
        _content.Height = height / _contentScale;
        _content.RenderTransform = new ScaleTransform { ScaleX = _contentScale, ScaleY = _contentScale };
    }

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

        // The tint and the border paint the same rounded box as the fill. The tint used to cover the
        // whole cell, gap included, so the Fluent lift spilled half a gap past every card.
        _tint.Margin = inset;
        _stroke.Margin = inset;
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

        // A widget a stack or tab strip lays out has no place of its own on the grid to drag: the
        // press goes on to the container's card, which moves the whole container.
        if (_host.ViewForCards is { } cards && DrawnByContainer(cards))
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

        LiveRect = GridGeometry.Settle(proposed, metrics, snap, _activeHandle);
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
            var cards = _host.ViewForCards;

            // [S-870] Holding widgets is what a container is for, so adding one inside comes first.
            if (cards is not null && WidgetTree.IsContainer(Model.Kind) && cards.CanAddInto(Model.Id))
            {
                menu.Items.Add(Item(Glyphs.Add, "Add widget inside…", () => _host.RequestAddInside(this)));
            }

            menu.Items.Add(Item("", "Configure…", () => _host.RequestConfigure(this)));
            // The view's own commands — pause a camera, refresh a page — sit under Configure,
            // read fresh each time so their text can follow the state they toggle.
            if (_view is IWidgetContextActions { ContextActions: { Count: > 0 } actions })
            {
                foreach (var action in actions)
                {
                    menu.Items.Add(Item(action.Glyph, action.Text, action.Invoke));
                }
            }

            menu.Items.Add(new MenuFlyoutSeparator());

            var move = BuildMoveMenu();
            if (move is not null)
            {
                menu.Items.Add(move);
            }

            // [S-874] Layering, for a widget the grid places (a stack or tab strip lays its own out).
            if (cards is not null && !DrawnByContainer(cards))
            {
                var front = Item(Glyphs.MoveUp, "Bring to front", () => _host.RequestZOrder(this, front: true));
                front.IsEnabled = cards.CanBringToFront(Model.Id);
                menu.Items.Add(front);

                var back = Item(Glyphs.MoveDown, "Send to back", () => _host.RequestZOrder(this, front: false));
                back.IsEnabled = cards.CanSendToBack(Model.Id);
                menu.Items.Add(back);
            }

            if (menu.Items.Count > 0 && menu.Items[^1] is not MenuFlyoutSeparator)
            {
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


    // ---- the keyboard lease ([S-800]) --------------------------------------------------------------

    /// <summary>[S-800] Whether a click here takes the keyboard: the view navigates with keys, and this is a real desktop card.</summary>
    public bool TakesKeys => _view is IWidgetKeys { WantsKeys: true } && !_host.IsPreview;

    /// <summary>
    /// [S-803] Hand the view a key from the lease. A view that throws is logged, not rethrown: the key
    /// arrives from the surface's input handler, and one widget's bug must not take the surface down.
    /// </summary>
    public bool SendKey(WidgetKey key)
    {
        if (_view is not IWidgetKeys keys)
        {
            return false;
        }

        try
        {
            return keys.HandleKey(key);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"{Model.Kind} threw handling {key}", ex);
            return false;
        }
    }

    private void OnLeasePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_host.EditMode || !OwnsPress(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _host.KeyboardPress(this);
    }

    /// <summary>
    /// Whether a press belongs to this card rather than to a widget inside it: a press in a stack's
    /// child bubbles up through the stack's card too, and only the innermost card is the one clicked.
    /// </summary>
    private bool OwnsPress(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is WidgetCard card)
            {
                return ReferenceEquals(card, this);
            }
        }

        return false;
    }

    // ---- IWidgetSurface ------------------------------------------------------------------------

    /// <inheritdoc />
    string? IWidgetSurface.Label => Model.Label;

    /// <inheritdoc />
    void IWidgetSurface.RequestSave() => _host.RequestSaveConfig();

    /// <inheritdoc />
    void IWidgetSurface.RequestTextInput(bool wanted) => _host.RequestTextInput(this, wanted);

    /// <inheritdoc />
    IWidgetDisplay? IWidgetSurface.Display => _host.Display;

    bool IWidgetSurface.IsPreview => _host.IsPreview;

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

    /// <inheritdoc />
    void IWidgetSurface.SetHidden(bool hidden)
    {
        if (_hidden == hidden)
        {
            return;
        }

        _hidden = hidden;
        ApplyHidden();
        ApplyDesign();
        _host.OnCardFrameChanged(this);
    }

    // ---- containers ([S-861]..[S-871]) ---------------------------------------------------------

    /// <summary>Whether a stack or tab strip lays this widget out, rather than the grid.</summary>
    private bool DrawnByContainer(ViewState view)
        => view.Find(Model.ParentId) is { } parent && WidgetTree.OwnsChildGeometry(WidgetTree.ContainerOf(parent.Kind));

    /// <summary>[S-861] A click on the header band of a view that owns its area (a group), not on a button in it.</summary>
    private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_host.EditMode || _view is not IWidgetArea area)
        {
            return;
        }

        for (var node = e.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, _headerHost); node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase)
            {
                return;
            }
        }

        area.BandTapped();
        e.Handled = true;
    }

    /// <inheritdoc />
    bool IContainerSurface.EditMode => _host.EditMode;

    /// <inheritdoc />
    HeaderPlacement IContainerSurface.Header => HeaderEdge;

    /// <inheritdoc />
    WidgetSurface IContainerSurface.Surface => Model.Surface;

    /// <inheritdoc />
    void IContainerSurface.SetCollapsed(bool collapsed)
    {
        if (_collapsed == collapsed)
        {
            return;
        }

        _collapsed = collapsed;
        ApplyHeader();
        _host.OnCardFrameChanged(this);
    }

    /// <inheritdoc />
    void IContainerSurface.ChildAction(string childId, ContainerChildAction action)
        => _host.RequestChildAction(this, childId, action);

    /// <inheritdoc />
    bool IContainerSurface.CanMoveChild(string childId, int delta)
        => _host.ViewForCards is { } view && view.CanMoveChild(childId, delta);

    /// <inheritdoc />
    void IContainerSurface.MoveChildTo(string childId, int index)
        => _host.RequestMoveChildTo(this, childId, index);

    /// <inheritdoc />
    void IContainerSurface.AddInside() => _host.RequestAddInside(this);

    /// <inheritdoc />
    void IContainerSurface.SetHeaderGlyph(string? glyph)
    {
        // The header's icon is the first glyph Fluent.Header put in it.
        if (_header.Children.OfType<FontIcon>().FirstOrDefault() is { } icon)
        {
            icon.Glyph = string.IsNullOrEmpty(glyph) ? WidgetCatalog.Definition(Model.Kind).Glyph : glyph;
        }
    }
}
