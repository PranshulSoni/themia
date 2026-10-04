using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
/// than rearranging them, so expanding puts every one back exactly where it was.
/// </para>
/// </summary>
internal sealed partial class GroupView : Grid, IWidgetView, IWidgetContainerView, IWidgetHeaderContent, IWidgetSurfaceAware
{
    private readonly Button _toggle = new();
    private readonly FontIcon _toggleIcon;
    private readonly TextBlock _count;

    private readonly List<FrameworkElement> _children = [];

    private GroupConfig _config = new();
    private IWidgetSurface? _surface;

    public GroupView()
    {
        _toggleIcon = Fluent.Icon(Glyphs.Done);

        _toggle.Content = _toggleIcon;
        _toggle.Padding = new Thickness(Fluent.Space1);
        _toggle.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _toggle.Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
        _toggle.BorderThickness = new Thickness(0);
        _toggle.Click += (_, _) => Toggle();

        _count = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        _count.VerticalAlignment = VerticalAlignment.Center;

        Trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children = { _count, _toggle },
        };
    }

    /// <summary>The child count and the collapse control, shown in the header the card draws.</summary>
    private StackPanel Trailing { get; }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => Trailing;

    public void Attach(Ticker ticker, WidgetConfig config) => ApplyConfig(config);

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as GroupConfig ?? new GroupConfig();
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

        _count.Text = _children.Count == 1 ? "1 widget" : $"{_children.Count} widgets";
        ApplyCollapse();
    }

    private void Toggle()
    {
        _config.Collapsed = !_config.Collapsed;
        ApplyCollapse();
        _surface?.RequestSave();
    }

    private void ApplyCollapse()
    {
        _toggleIcon.Glyph = _config.Collapsed ? Glyphs.Add : Glyphs.Done;
        ToolTipService.SetToolTip(_toggle, _config.Collapsed ? "Show these widgets" : "Hide these widgets");

        foreach (var child in _children)
        {
            child.Visibility = _config.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        }
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
