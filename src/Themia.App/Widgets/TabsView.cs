using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell;
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
/// </summary>
internal sealed partial class TabsView : Grid, IWidgetView, IWidgetContainerView, IWidgetSurfaceAware
{
    private readonly StackPanel _strip = new();
    private readonly ScrollViewer _stripScroll;
    private readonly Grid _body = new();
    private readonly TextBlock _empty;
    private readonly List<IDisposable> _hovers = [];
    private readonly List<FrameworkElement> _children = [];

    private TabsConfig _config = new();
    private IWidgetSurface? _surface;

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

        _empty = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        _empty.Text = "Nothing in here yet. Drag a widget onto it in edit mode.";
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextTrimming = TextTrimming.None;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, 0);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(_stripScroll, 0);
        Grid.SetRow(_body, 1);
        Grid.SetRow(_empty, 1);

        Children.Add(_stripScroll);
        Children.Add(_body);
        Children.Add(_empty);
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config) => ApplyConfig(config);

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as TabsConfig ?? new TabsConfig();
        Rebuild();
    }

    /// <inheritdoc />
    public void SetChildren(IReadOnlyList<FrameworkElement> children)
    {
        ArgumentNullException.ThrowIfNull(children);

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
        ClearTabs();

        var any = _children.Count > 0;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _stripScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _body.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        if (!any)
        {
            return;
        }

        var selected = SelectedIndex();

        for (var i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            var isSelected = i == selected;

            // Exactly one child is visible, and the rest are collapsed rather than hidden behind it:
            // a widget nobody can see should not be measuring itself on every pass.
            child.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            _body.Children.Add(child);

            var index = i;
            var tab = Tab(child, isSelected);
            tab.Tapped += (_, _) => Select(index);
            _hovers.Add(Fluent.Hover(tab));
            _strip.Children.Add(tab);
        }
    }

    private int SelectedIndex()
    {
        if (_config.SelectedChildId is { } id)
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

    /// <summary>
    /// One tab. The selected one carries an accent underline rather than a filled pill, which is what
    /// Windows does and what keeps the strip readable when several tabs are open.
    /// </summary>
    private static Border Tab(FrameworkElement child, bool isSelected)
    {
        var name = child is WidgetCard card
            ? (string.IsNullOrWhiteSpace(card.Model.Label)
                ? WidgetCatalog.Definition(card.Model.Kind).Label
                : card.Model.Label)
            : "Widget";

        var text = Fluent.Text(
            isSelected ? FluentType.BodyStrong : FluentType.Body,
            isSelected ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");

        text.Text = name;
        text.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            Child = text,
            MinHeight = Fluent.RowHeightCompact,
            Padding = new Thickness(Fluent.Space3, 0, Fluent.Space3, 0),
            Background = Fluent.RowRest(),
            BorderThickness = new Thickness(0, 0, 0, isSelected ? 2 : 0),
            BorderBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
        };
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

    private void ClearTabs()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _strip.Children.Clear();

        // The children belong to the surface; letting go of them here is what lets them return to
        // the grid when they leave.
        _body.Children.Clear();
    }

    public void Detach()
    {
        ClearTabs();

        foreach (var child in _children)
        {
            child.Visibility = Visibility.Visible;
        }

        _children.Clear();
    }
}
