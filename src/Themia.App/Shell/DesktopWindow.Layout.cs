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
/// Layout: which view this display shows, positioning the cards in it, containers, the hit region
/// and the snap grid overlay.
/// </summary>
internal sealed partial class DesktopWindow
{
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
}
