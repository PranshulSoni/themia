using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// A labelled area other widgets belong to.
/// <para>
/// The odd one of the three containers: it does not lay its children out. They keep the place on the
/// grid the user chose and this draws behind them, which is why dropping a widget into a group does
/// not move it. All the group does is collapse — and collapsing <b>hides</b> the children rather
/// than rearranging them, so expanding puts every one back exactly where it was — while the card
/// itself shrinks to its title bar ([S-871]).
/// </para>
/// <para>
/// [S-863] The body is the area: a faint wash (or the user's fill) inside the card's border, no
/// padding, nothing drawn over the members. The card paints its surface behind the bar only
/// (<see cref="IWidgetArea"/>). There is no member count ([S-05] of the original: it made every area
/// look like an inbox).
/// </para>
/// </summary>
internal sealed partial class GroupView : Grid, IWidgetView, IWidgetContainerView, IWidgetHeaderContent,
    IWidgetSurfaceAware, IWidgetArea, IContainerChrome, IWidgetSizeAware
{
    private readonly Button _toggle = new();
    private readonly FontIcon _toggleIcon;
    private readonly ContainerPlaceholder _empty;

    private readonly List<FrameworkElement> _children = [];

    private GroupConfig _config = new();
    private IWidgetSurface? _surface;

    public GroupView()
    {
        _toggleIcon = Fluent.Icon(Glyphs.ChevronUp, "TextFillColorSecondaryBrush", 12);

        _toggle.Content = _toggleIcon;
        _toggle.Width = WidgetCard.HeaderControlHeight;
        _toggle.Height = WidgetCard.HeaderControlHeight;
        _toggle.MinWidth = 0;
        _toggle.MinHeight = 0;
        _toggle.Padding = new Thickness(0);
        _toggle.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _toggle.Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
        _toggle.BorderThickness = new Thickness(0);

        // [S-861] The button works in both modes: a collapsed group hides its members, and edit mode
        // is exactly where the user needs them back. The band (the rest of the bar) only at rest.
        _toggle.Click += (_, _) => Toggle();

        _empty = new ContainerPlaceholder(WidgetCatalog.Definition(WidgetKind.Group).Glyph, () => Container?.AddInside());
        Children.Add(_empty);
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _toggle;

    private IContainerSurface? Container => _surface as IContainerSurface;

    public void Attach(Ticker ticker, WidgetConfig config) => ApplyConfig(config);

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as GroupConfig ?? new GroupConfig();
        Container?.SetHeaderGlyph(_config.Icon);
        ApplyCollapse();
    }

    /// <inheritdoc />
    public void SetChildren(IReadOnlyList<FrameworkElement> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        // A child that has left the group must not be left hidden by it: whatever this group was
        // holding is made visible again before the new set is taken.
        Reveal();

        _children.Clear();
        _children.AddRange(children);
        ApplyCollapse();
    }

    /// <inheritdoc />
    public bool BandOnly => Container is { } container && GroupRules.BandOnly(_config, container.Surface, _children.Count);

    /// <inheritdoc />
    public void BandTapped() => Toggle();

    /// <inheritdoc />
    public void ApplyEditMode(bool editing) => _empty.ApplyEditMode(editing);

    /// <inheritdoc />
    public void ApplySize(WidgetArea area) => _empty.ApplySize(area);

    private void Toggle()
    {
        _config.Collapsed = !_config.Collapsed;
        ApplyCollapse();
        _surface?.RequestSave();
    }

    private void ApplyCollapse()
    {
        var collapsed = _config.Collapsed;
        var header = Container?.Header ?? HeaderPlacement.Top;

        // [S-861] The chevron points at the space the area opens into while closed, and back at the
        // bar while open. A side bar is the top bar turned, so left reads like top and right like
        // bottom.
        var towardsBody = header is HeaderPlacement.Bottom or HeaderPlacement.Right ? Glyphs.ChevronUp : Glyphs.ChevronDown;
        var towardsBar = header is HeaderPlacement.Bottom or HeaderPlacement.Right ? Glyphs.ChevronDown : Glyphs.ChevronUp;
        _toggleIcon.Glyph = collapsed ? towardsBody : towardsBar;
        ToolTipService.SetToolTip(_toggle, collapsed ? "Show these widgets" : "Hide these widgets");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_toggle, collapsed ? "Expand group" : "Collapse group");

        foreach (var child in _children)
        {
            child.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        _empty.Visibility = !collapsed && _children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Background = collapsed ? null : AreaBrush();
        Container?.SetCollapsed(collapsed);
    }

    /// <summary>
    /// [S-863] The area's paint: the user's colour at their opacity, else the theme's faint wash —
    /// cleared when the widget's background is "None", which is how an area is made see-through.
    /// </summary>
    private Brush? AreaBrush()
    {
        if (GroupRules.Fill(_config) is { } fill)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb((byte)Math.Round(fill.Opacity * 255), fill.R, fill.G, fill.B));
        }

        return Container?.Surface == WidgetSurface.Transparent
            ? null
            : FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Transparent);
    }

    private void Reveal()
    {
        foreach (var child in _children)
        {
            child.Visibility = Visibility.Visible;
        }
    }

    public void Detach()
    {
        // Leaving the desktop must not leave a dozen widgets invisible with nothing left to un-hide
        // them.
        Reveal();
        _children.Clear();
    }
}
