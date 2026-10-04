using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// An accordion: every child is a row, and one of them is open.
/// <para>
/// Unlike a group, this owns its children's geometry — their stored rect means nothing while they
/// are in here, and means what it always did the moment they leave. Nothing is written back to it,
/// so taking a widget out of a stack puts it exactly where it was before it went in.
/// </para>
/// </summary>
internal sealed partial class StackView : Grid, IWidgetView, IWidgetContainerView, IWidgetSurfaceAware
{
    private readonly StackPanel _rows = new();
    private readonly StackPanel _empty;
    private readonly List<IDisposable> _hovers = [];
    private readonly List<FrameworkElement> _children = [];

    private StackConfig _config = new();
    private IWidgetSurface? _surface;

    public StackView()
    {
        // [S-642] The shared empty state rather than a bare text block of its own, so it sheds to
        // fit a small tile like every other one instead of running off the top and bottom of it.
        _empty = Fluent.EmptyState(
            WidgetCatalog.Definition(WidgetKind.Stack).Glyph,
            new TextBlock { Text = "Nothing in here yet. Drag a widget onto it in edit mode." },
            out _);

        _rows.Orientation = Orientation.Vertical;

        Children.Add(_rows);
        Children.Add(_empty);
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config) => ApplyConfig(config);

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as StackConfig ?? new StackConfig();
        Rebuild();
    }

    /// <inheritdoc />
    public void SetChildren(IReadOnlyList<FrameworkElement> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        // Same children in the same order is the common case on a layout pass, and rebuilding then
        // would drop the row under the pointer and restart the open child's animation.
        if (Same(children))
        {
            return;
        }

        _children.Clear();
        _children.AddRange(children);
        Rebuild();
    }

    private bool Same(IReadOnlyList<FrameworkElement> children)
    {
        if (children.Count != _children.Count)
        {
            return false;
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (!ReferenceEquals(children[i], _children[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Rebuild()
    {
        ClearRows();

        _empty.Visibility = _children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _rows.Visibility = _children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (_children.Count == 0)
        {
            return;
        }

        var open = OpenIndex();

        for (var i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            var isOpen = i == open;

            // The row is the handle; the child is what it opens. A closed child is collapsed rather
            // than merely short, so a widget nobody can see is not laying itself out or ticking a
            // layout pass it cannot be read from.
            child.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;

            var index = i;
            var row = Fluent.Row(Title(child, isOpen), compact: true);
            row.Tapped += (_, _) => Open(index);
            _hovers.Add(Fluent.Hover(row));

            _rows.Children.Add(row);
            _rows.Children.Add(child);
        }
    }

    /// <summary>
    /// Which child is open. An id naming a child that is gone opens the first one: a stack with
    /// everything rolled up looks broken rather than tidy.
    /// </summary>
    private int OpenIndex()
    {
        if (_config.OpenChildId is { } id)
        {
            for (var i = 0; i < _children.Count; i++)
            {
                if (_children[i] is WidgetCard card && string.Equals(card.Model.Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return 0;
    }

    private static FrameworkElement Title(FrameworkElement child, bool isOpen)
    {
        var name = child is WidgetCard card
            ? (string.IsNullOrWhiteSpace(card.Model.Label)
                ? WidgetCatalog.Definition(card.Model.Kind).Label
                : card.Model.Label)
            : "Widget";

        var chevron = Fluent.Icon(isOpen ? Glyphs.MoveUp : Glyphs.MoveDown, "TextFillColorTertiaryBrush", 12);
        var text = Fluent.Text(isOpen ? FluentType.BodyStrong : FluentType.Body, "TextFillColorPrimaryBrush");
        text.Text = name;
        text.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid
        {
            ColumnSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(text, 0);
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(text);
        grid.Children.Add(chevron);
        return grid;
    }

    private void Open(int index)
    {
        if (index < 0 || index >= _children.Count)
        {
            return;
        }

        _config.OpenChildId = _children[index] is WidgetCard card ? card.Model.Id : null;
        Rebuild();
        _surface?.RequestSave();
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();

        // The children belong to the surface, not to this panel: detaching them here is what lets
        // them go back onto the grid when they leave the stack.
        _rows.Children.Clear();
    }

    public void Detach()
    {
        ClearRows();

        // A child left collapsed would be invisible on the desktop with nothing to reveal it.
        foreach (var child in _children)
        {
            child.Visibility = Visibility.Visible;
        }

        _children.Clear();
    }
}
