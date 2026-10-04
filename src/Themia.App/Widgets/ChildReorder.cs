using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// [S-866] Reorder a container's children by dragging their rows (or tabs) in edit mode. The dragged
/// handle follows the pointer along one axis; on release the child goes to the slot the pointer is
/// over, through one move of the view's order. Outside edit mode a press is left alone, so the rows
/// keep toggling. The press is marked handled in edit mode, so the container's card does not start
/// dragging the whole container from a row.
/// </summary>
internal sealed class ChildReorder : IDisposable
{
    private readonly UIElement _panel;
    private readonly Orientation _axis;
    private readonly Func<bool> _editing;
    private readonly Action<string, int> _moved;
    private readonly List<(FrameworkElement Handle, string Id)> _handles = [];

    private FrameworkElement? _dragged;
    private string? _draggedId;
    private int _from;
    private double _start;
    private double[] _centres = [];

    /// <param name="panel">What the handles' positions are measured against.</param>
    /// <param name="axis">Rows drag vertically, tabs horizontally.</param>
    /// <param name="editing">Whether the surface is in edit mode now.</param>
    /// <param name="moved">A child id and the index among its siblings it was dropped at.</param>
    public ChildReorder(UIElement panel, Orientation axis, Func<bool> editing, Action<string, int> moved)
    {
        _panel = panel;
        _axis = axis;
        _editing = editing;
        _moved = moved;
    }

    /// <summary>Make <paramref name="handle"/> drag child <paramref name="id"/>. Handles are added in sibling order.</summary>
    public void Add(FrameworkElement handle, string id)
    {
        handle.PointerPressed += OnPressed;
        handle.PointerMoved += OnMoved;
        handle.PointerReleased += OnReleased;
        handle.PointerCaptureLost += OnCaptureLost;
        _handles.Add((handle, id));
    }

    /// <summary>Unhook every handle; call before the rows are rebuilt and on Detach.</summary>
    public void Dispose()
    {
        foreach (var (handle, _) in _handles)
        {
            handle.PointerPressed -= OnPressed;
            handle.PointerMoved -= OnMoved;
            handle.PointerReleased -= OnReleased;
            handle.PointerCaptureLost -= OnCaptureLost;
            handle.RenderTransform = null;
        }

        _handles.Clear();
        _dragged = null;
    }

    private double Along(Point point) => _axis == Orientation.Vertical ? point.Y : point.X;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_editing() || sender is not FrameworkElement handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var index = _handles.FindIndex(h => ReferenceEquals(h.Handle, handle));
        if (index < 0 || !handle.CapturePointer(e.Pointer))
        {
            return;
        }

        // Where every handle's centre is before anything moves: the drop slot is read against these.
        _centres = [.. _handles.Select(h =>
        {
            var origin = h.Handle.TransformToVisual(_panel).TransformPoint(new Point(0, 0));
            return Along(origin) + ((_axis == Orientation.Vertical ? h.Handle.ActualHeight : h.Handle.ActualWidth) / 2);
        })];

        _dragged = handle;
        _draggedId = _handles[index].Id;
        _from = index;
        _start = Along(e.GetCurrentPoint(_panel).Position);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragged is null || !ReferenceEquals(sender, _dragged))
        {
            return;
        }

        var offset = Along(e.GetCurrentPoint(_panel).Position) - _start;
        _dragged.RenderTransform = _axis == Orientation.Vertical
            ? new TranslateTransform { Y = offset }
            : new TranslateTransform { X = offset };
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragged is null || !ReferenceEquals(sender, _dragged))
        {
            return;
        }

        var at = Along(e.GetCurrentPoint(_panel).Position);
        var id = _draggedId;
        var from = _from;
        _dragged.ReleasePointerCapture(e.Pointer);
        End();
        e.Handled = true;

        var to = Themia.Core.Widgets.ContainerLayout.DropSlot(_centres, from, at);
        if (id is not null && to != from)
        {
            _moved(id, to);
        }
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _dragged))
        {
            End();
        }
    }

    private void End()
    {
        if (_dragged is not null)
        {
            _dragged.RenderTransform = null;
        }

        _dragged = null;
        _draggedId = null;
    }
}
