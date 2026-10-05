using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// An accordion: every child is a row, and at most one of them is open.
/// <para>
/// Unlike a group, this owns its children's geometry — their stored rect means nothing while they
/// are in here, and means what it always did the moment they leave. Nothing is written back to it,
/// so taking a widget out of a stack puts it exactly where it was before it went in.
/// </para>
/// <para>
/// [S-864] Every child starts closed; a row click opens that child and closes the open one, a click
/// on the open child's row closes it. [S-867] Each row is the stack's own — icon, name, chevron — and
/// the open child is drawn under it as a bare body (the card hides its header), so no row ever
/// depends on how a child lays out its own header. [S-865] The ladder is <see cref="ContainerLayout.Plan"/>.
/// </para>
/// </summary>
internal sealed partial class StackView : Grid, IWidgetView, IWidgetContainerView, IWidgetSurfaceAware,
    IContainerChrome, IWidgetSizeAware, IWidgetKeys
{
    private readonly ContainerPlaceholder _empty;
    private readonly List<IDisposable> _hovers = [];
    private readonly List<FrameworkElement> _children = [];
    private readonly List<Border> _rows = [];

    /// <summary>Holds the rows (and the open child) in whichever arrangement the ladder picked.</summary>
    private readonly Grid _host = new();

    /// <summary>The row strip of the wide arrangement; never a child card (cards are Grids too).</summary>
    private Grid? _strip;

    private ChildReorder? _reorder;
    private StackConfig _config = new();
    private IWidgetSurface? _surface;
    private WidgetArea _area;
    private StackPlan? _plan;
    private int _cursor = -1;

    public StackView()
    {
        _empty = new ContainerPlaceholder(WidgetCatalog.Definition(WidgetKind.Stack).Glyph, () => Container?.AddInside());
        Children.Add(_host);
        Children.Add(_empty);
    }

    private IContainerSurface? Container => _surface as IContainerSurface;

    private bool Editing => Container?.EditMode ?? false;

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        Strings.Changed += OnLanguage;
        ApplyConfig(config);
    }

    /// <summary>[S-1109] The rows carry catalogue names, which follow the language.</summary>
    private void OnLanguage(Locale locale) => Rebuild();

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
        if (children.SequenceEqual(_children))
        {
            return;
        }

        _children.Clear();
        _children.AddRange(children);
        Rebuild();
    }

    /// <inheritdoc />
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        _empty.ApplySize(area);

        // Only a different plan rebuilds: the open child's height is a star row and follows by itself.
        if (Plan() != _plan)
        {
            Rebuild();
        }
    }

    /// <inheritdoc />
    public void ApplyEditMode(bool editing) => _empty.ApplyEditMode(editing);

    private List<string> Ids() => [.. _children.Select(c => c is WidgetCard card ? card.Model.Id : string.Empty)];

    private int OpenIndex() => ContainerRules.OpenIndex(Ids(), _config.OpenChildId);

    private StackPlan Plan()
        => ContainerLayout.Plan(_area.IsMeasured ? _area : new WidgetArea(320, 240), _children.Count, OpenIndex() >= 0, Fluent.RowHeight);

    private void Rebuild()
    {
        ClearRows();

        var any = _children.Count > 0;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _host.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        if (!any)
        {
            _plan = null;
            return;
        }

        var plan = Plan();
        _plan = plan;
        var open = OpenIndex();
        var strip = plan.Arrangement == StackArrangement.Strip;

        var rowsPanel = new Grid();
        _strip = rowsPanel;
        for (var i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            var isOpen = i == open;

            // [S-414] A closed child is collapsed rather than merely short, so a widget nobody can
            // see is not laying itself out; an open one too small to read is not drawn either.
            child.Visibility = isOpen && plan.BodyShown ? Visibility.Visible : Visibility.Collapsed;

            var row = Row(child, isOpen, plan.Icons, i);
            _rows.Add(row);

            if (strip)
            {
                rowsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(row, i);
                rowsPanel.Children.Add(row);
            }
            else
            {
                // Rows down the tile, the open child in a star row under its own.
                _host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(row, _host.RowDefinitions.Count - 1);
                _host.Children.Add(row);

                if (isOpen && plan.BodyShown)
                {
                    _host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    Grid.SetRow(child, _host.RowDefinitions.Count - 1);
                    _host.Children.Add(child);
                }
            }
        }

        if (strip)
        {
            // [S-865] A wide strip: the rows side by side along the top, the open child under them all.
            _host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _host.Children.Add(rowsPanel);
            if (open >= 0 && plan.BodyShown)
            {
                Grid.SetRow(_children[open], 1);
                _host.Children.Add(_children[open]);
            }
        }
        else if (open < 0 || !plan.BodyShown)
        {
            // Nothing below the rows: a filler row keeps them at the top.
            _host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        _reorder = new ChildReorder(strip ? rowsPanel : _host, strip ? Orientation.Horizontal : Orientation.Vertical,
            () => Editing, (id, index) => Container?.MoveChildTo(id, index));
        for (var i = 0; i < _rows.Count; i++)
        {
            _reorder.Add(_rows[i], Ids()[i]);
        }

        MarkCursor();
    }

    /// <summary>[S-867] One row: the child's icon (from S), its name and a chevron that turns when it opens.</summary>
    private Border Row(FrameworkElement child, bool isOpen, bool icons, int index)
    {
        var card = child as WidgetCard;
        var name = card is null ? Str.Settings_Style_Widget.Text()
            : string.IsNullOrWhiteSpace(card.Model.Label) ? WidgetCatalog.Definition(card.Model.Kind).Label
            : card.Model.Label;

        var text = Fluent.Text(isOpen ? FluentType.BodyStrong : FluentType.Body);
        text.Text = name;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.TextWrapping = TextWrapping.NoWrap;

        var chevron = Fluent.Icon(isOpen ? Glyphs.ChevronUp : Glyphs.ChevronDown, "TextFillColorTertiaryBrush", 12);

        var grid = new Grid
        {
            ColumnSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        if (icons && card is not null)
        {
            var icon = Fluent.Icon(WidgetCatalog.Definition(card.Model.Kind).Glyph);
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
        }

        Grid.SetColumn(text, 1);
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(text);
        grid.Children.Add(chevron);

        var row = Fluent.Row(grid);
        RowFocus.Prepare(row);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, name);

        // [S-864] Clicks on the row band only: the open child's own clicks never reach here, since
        // it is a sibling of the row, not inside it. Inert in edit mode: the card owns the pointer.
        row.Tapped += (_, e) =>
        {
            if (!Editing)
            {
                _cursor = index;
                Toggle(index);
                e.Handled = true;
            }
        };

        row.RightTapped += (_, e) => ItemMenu.Show(row, e, Menu(index));
        _hovers.Add(Fluent.Hover(row));
        return row;
    }

    /// <summary>[S-866] A row's menu: the child's settings, move up/down (not past the ends), take it out.</summary>
    private List<ItemMenuEntry> Menu(int index)
    {
        if (index < 0 || index >= _children.Count || _children[index] is not WidgetCard card || Container is not { } container)
        {
            return [];
        }

        var id = card.Model.Id;
        return
        [
            new(Glyphs.Settings, Str.ViewSwitcher_Configure.Text(), () => container.ChildAction(id, ContainerChildAction.Configure)),
            ItemMenuEntry.Separator,
            new(Glyphs.MoveUp, Str.Stack_MoveUp.Text(), () => container.ChildAction(id, ContainerChildAction.MoveUp), Disabled: !container.CanMoveChild(id, -1)),
            new(Glyphs.MoveDown, Str.Stack_MoveDown.Text(), () => container.ChildAction(id, ContainerChildAction.MoveDown), Disabled: !container.CanMoveChild(id, 1)),
            ItemMenuEntry.Separator,
            new(Glyphs.TakeOut, Str.WidgetsA_Stack_TakeOut.Text(), () => container.ChildAction(id, ContainerChildAction.TakeOut)),
        ];
    }

    private void Toggle(int index)
    {
        if (index < 0 || index >= _children.Count)
        {
            return;
        }

        _config.OpenChildId = ContainerRules.Toggle(Ids(), _config.OpenChildId, index);
        Rebuild();
        _surface?.RequestSave();
    }

    // ---- keys ([S-865], the original's Enter/Space on a row) -------------------------------------

    /// <inheritdoc />
    public bool HandleKey(WidgetKey key)
    {
        if (_children.Count == 0)
        {
            return false;
        }

        switch (key)
        {
            case WidgetKey.Up or WidgetKey.Left:
                _cursor = _cursor < 0 ? 0 : Math.Max(0, _cursor - 1);
                break;
            case WidgetKey.Down or WidgetKey.Right:
                _cursor = _cursor < 0 ? 0 : Math.Min(_children.Count - 1, _cursor + 1);
                break;
            case WidgetKey.Home:
                _cursor = 0;
                break;
            case WidgetKey.End:
                _cursor = _children.Count - 1;
                break;
            case WidgetKey.Enter or WidgetKey.Space when _cursor >= 0:
                Toggle(_cursor);
                return true;
            case WidgetKey.Menu when _cursor >= 0 && _cursor < _rows.Count:
                ItemMenu.Open(_rows[_cursor], Menu(_cursor));
                return true;
            case WidgetKey.Escape:
                _cursor = -1;
                break;
            default:
                return false;
        }

        MarkCursor();
        return true;
    }

    private void MarkCursor()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            RowFocus.Mark(_rows[i], i == _cursor);
        }
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _reorder?.Dispose();
        _reorder = null;
        _rows.Clear();

        // The children belong to the surface, not to this panel: detaching them here is what lets
        // them go back onto the grid when they leave the stack.
        _strip?.Children.Clear();
        _strip = null;
        _host.Children.Clear();
        _host.RowDefinitions.Clear();
    }

    public void Detach()
    {
        Strings.Changed -= OnLanguage;
        ClearRows();

        // A child left collapsed would be invisible on the desktop with nothing to reveal it.
        foreach (var child in _children)
        {
            child.Visibility = Visibility.Visible;
        }

        _children.Clear();
    }
}
