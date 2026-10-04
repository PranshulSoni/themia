using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// Several widgets in one rect, one shown at a time.
/// <para>
/// A sibling of <see cref="StackView"/>: same containment, different arrangement. Both own their
/// children's geometry and neither writes anything back to it, so a widget taken out of either goes
/// back to exactly the place on the grid it had before it went in.
/// </para>
/// <para>
/// [S-868] The shown tab is the selected child, else the first; a stale id is ignored, not
/// rewritten. Unlike a stack row, a tab does not replace the child's header: the shown child draws
/// its own live header under the strip, as the original did. [S-869] Tabs carry icon and name on a
/// wide tile, the name where it fits, the icon alone where only that fits.
/// </para>
/// </summary>
internal sealed partial class TabsView : Grid, IWidgetView, IWidgetContainerView, IWidgetSurfaceAware,
    IContainerChrome, IWidgetSizeAware, IWidgetKeys
{
    /// <summary>A tab's inner padding on each side.</summary>
    private const double TabPadding = Fluent.Space3;

    private readonly StackPanel _strip = new();
    private readonly ScrollViewer _stripScroll;
    private readonly Grid _body = new();
    private readonly ContainerPlaceholder _empty;
    private readonly List<IDisposable> _hovers = [];
    private readonly List<FrameworkElement> _children = [];
    private readonly List<Border> _tabs = [];

    private ChildReorder? _reorder;
    private TabsConfig _config = new();
    private IWidgetSurface? _surface;
    private WidgetArea _area;
    private TabLabels? _labels;

    public TabsView()
    {
        _strip.Orientation = Orientation.Horizontal;
        _strip.Spacing = Fluent.Space1;
        _strip.Padding = new Thickness(Fluent.Space2, 0, Fluent.Space2, 0);

        _stripScroll = new ScrollViewer
        {
            Content = _strip,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        _empty = new ContainerPlaceholder(WidgetCatalog.Definition(WidgetKind.Tabs).Glyph, () => Container?.AddInside());

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(_stripScroll, 0);
        Grid.SetRow(_body, 1);
        Grid.SetRowSpan(_empty, 2);

        Children.Add(_stripScroll);
        Children.Add(_body);
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

    /// <summary>[S-1109] The tabs carry catalogue names, which follow the language.</summary>
    private void OnLanguage(Locale locale) => Rebuild();

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as TabsConfig ?? new TabsConfig();
        Rebuild();
    }

    /// <inheritdoc />
    public void SetChildren(IReadOnlyList<FrameworkElement> children)
    {
        ArgumentNullException.ThrowIfNull(children);

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

        if (Labels() != _labels)
        {
            Rebuild();
        }
    }

    /// <inheritdoc />
    public void ApplyEditMode(bool editing) => _empty.ApplyEditMode(editing);

    private List<string> Ids() => [.. _children.Select(c => c is WidgetCard card ? card.Model.Id : string.Empty)];

    private static string ChildName(FrameworkElement child) => child is WidgetCard card
        ? (string.IsNullOrWhiteSpace(card.Model.Label) ? WidgetCatalog.Definition(card.Model.Kind).Label : card.Model.Label)
        : Str.Settings_Style_Widget.Text();

    /// <summary>[S-869] How the tabs are labelled at this width; names are measured at their worst case (Body Strong).</summary>
    private TabLabels Labels()
    {
        var area = _area.IsMeasured ? _area : new WidgetArea(320, 240);
        var names = _children.Select(c => FluentType.EstimateWidth(FluentType.BodyStrong, ChildName(c).Length)).ToList();
        return ContainerLayout.Tabs(area.Width - (2 * Fluent.Space2), area.WidthClass, names, Fluent.IconSize,
            (2 * TabPadding) + Fluent.Space1 + Fluent.Space2);
    }

    private void Rebuild()
    {
        ClearTabs();

        var any = _children.Count > 0;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _stripScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _body.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        if (!any)
        {
            _labels = null;
            return;
        }

        var labels = Labels();
        _labels = labels;
        var selected = ContainerRules.SelectedIndex(Ids(), _config.SelectedChildId);
        _reorder = new ChildReorder(_strip, Orientation.Horizontal, () => Editing, (id, index) => Container?.MoveChildTo(id, index));

        for (var i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            var isSelected = i == selected;

            // Exactly one child is visible, and the rest are collapsed rather than hidden behind it:
            // a widget nobody can see should not be measuring itself on every pass.
            child.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            _body.Children.Add(child);

            var tab = Tab(child, isSelected, labels, i);
            _tabs.Add(tab);
            _strip.Children.Add(tab);
            _reorder.Add(tab, Ids()[i]);
        }
    }

    /// <summary>
    /// One tab. The selected one carries an accent underline rather than a filled pill, which is what
    /// Windows does and what keeps the strip readable when several tabs are open.
    /// </summary>
    private Border Tab(FrameworkElement child, bool isSelected, TabLabels labels, int index)
    {
        var name = ChildName(child);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space2 };

        if (labels != TabLabels.Text && child is WidgetCard card)
        {
            var icon = Fluent.Icon(WidgetCatalog.Definition(card.Model.Kind).Glyph,
                isSelected ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            icon.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(icon);
        }

        if (labels != TabLabels.Icon)
        {
            var text = Fluent.Text(
                isSelected ? FluentType.BodyStrong : FluentType.Body,
                isSelected ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            text.Text = name;
            text.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(text);
        }
        else
        {
            ToolTipService.SetToolTip(content, name);
        }

        var tab = new Border
        {
            Child = content,
            MinHeight = Fluent.RowHeightCompact,
            Padding = labels == TabLabels.Icon ? new Thickness(Fluent.Space2, 0, Fluent.Space2, 0) : new Thickness(TabPadding, 0, TabPadding, 0),
            Background = Fluent.RowRest(),
            BorderThickness = new Thickness(0, 0, 0, isSelected ? 2 : 0),
            BorderBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tab, name);

        // Inert in edit mode: the card owns the pointer there, and a drag reorders instead.
        tab.Tapped += (_, e) =>
        {
            if (!Editing)
            {
                Select(index);
                e.Handled = true;
            }
        };

        tab.RightTapped += (_, e) => ItemMenu.Show(tab, e, Menu(index));
        _hovers.Add(Fluent.Hover(tab));
        return tab;
    }

    /// <summary>[S-866] A tab's menu: the child's settings, move left/right (not past the ends), take it out.</summary>
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
            new(Glyphs.MoveUp, Str.WidgetsA_Tabs_MoveLeft.Text(), () => container.ChildAction(id, ContainerChildAction.MoveUp), Disabled: !container.CanMoveChild(id, -1)),
            new(Glyphs.MoveDown, Str.WidgetsA_Tabs_MoveRight.Text(), () => container.ChildAction(id, ContainerChildAction.MoveDown), Disabled: !container.CanMoveChild(id, 1)),
            ItemMenuEntry.Separator,
            new(Glyphs.TakeOut, Str.WidgetsA_Tabs_TakeOut.Text(), () => container.ChildAction(id, ContainerChildAction.TakeOut)),
        ];
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _children.Count)
        {
            return;
        }

        _config.SelectedChildId = _children[index] is WidgetCard card ? card.Model.Id : null;
        Rebuild();
        _surface?.RequestSave();
    }

    /// <inheritdoc />
    public bool HandleKey(WidgetKey key)
    {
        if (_children.Count == 0)
        {
            return false;
        }

        var selected = ContainerRules.SelectedIndex(Ids(), _config.SelectedChildId);
        var next = key switch
        {
            WidgetKey.Left => Math.Max(0, selected - 1),
            WidgetKey.Right => Math.Min(_children.Count - 1, selected + 1),
            WidgetKey.Home => 0,
            WidgetKey.End => _children.Count - 1,
            _ => -1,
        };

        if (next < 0)
        {
            return false;
        }

        if (next != selected)
        {
            Select(next);
        }

        return true;
    }

    private void ClearTabs()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _reorder?.Dispose();
        _reorder = null;
        _tabs.Clear();
        _strip.Children.Clear();

        // The children belong to the surface; letting go of them here is what lets them return to
        // the grid when they leave.
        _body.Children.Clear();
    }

    public void Detach()
    {
        Strings.Changed -= OnLanguage;
        ClearTabs();

        foreach (var child in _children)
        {
            child.Visibility = Visibility.Visible;
        }

        _children.Clear();
    }
}
