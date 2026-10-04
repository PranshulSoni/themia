using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-870] What an empty container shows: a dashed edge, an "Add widget" button (in both modes — an
/// empty container is what every one starts as, and an invisible one reads as broken) and, in edit
/// mode only, the hint that widgets can be dragged in, since dragging in is an edit-mode gesture.
/// Shared by group, stack and tabs so the three agree.
/// </summary>
internal sealed partial class ContainerPlaceholder : Grid
{
    private readonly Rectangle _edge;
    private readonly FontIcon _icon;
    private readonly Button _add;
    private readonly TextBlock _addText;
    private readonly TextBlock _hint;

    private bool _editing;
    private WidgetArea _area;

    public ContainerPlaceholder(string glyph, Action add)
    {
        ArgumentNullException.ThrowIfNull(add);

        _edge = new Rectangle
        {
            Stroke = FluentLayout.Themed("ControlStrongStrokeColorDefaultBrush", Colors.Gray),
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            RadiusX = Fluent.ControlRadius,
            RadiusY = Fluent.ControlRadius,
            Margin = new Thickness(Fluent.Space2),
            Opacity = 0.6,
            IsHitTestVisible = false,
        };

        _icon = Fluent.Icon(glyph, "TextFillColorTertiaryBrush", Fluent.Space6);
        _icon.HorizontalAlignment = HorizontalAlignment.Center;

        _addText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _add = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { Fluent.Icon(Glyphs.Add, "TextFillColorPrimaryBrush", 14), _addText },
            },
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _add.Click += (_, _) => add();

        _hint = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.TextWrapping = TextWrapping.Wrap;

        Children.Add(_edge);
        Children.Add(new StackPanel
        {
            Spacing = Fluent.Space2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(Fluent.Space3),
            Children = { _icon, _add, _hint },
        });

        ApplyTexts();
        Lay();

        // [S-1109] Follows the language while it is on screen; a static event, so only then.
        Loaded += (_, _) => Strings.Changed += OnLanguage;
        Unloaded += (_, _) => Strings.Changed -= OnLanguage;
    }

    private void OnLanguage(Locale locale) => ApplyTexts();

    private void ApplyTexts()
    {
        _addText.Text = Str.Group_AddChild.Text();
        ToolTipService.SetToolTip(_add, Str.WidgetsA_Container_AddTooltip.Text());
        _hint.Text = Str.WidgetsA_Container_DragHint.Text();
    }

    public void ApplyEditMode(bool editing)
    {
        _editing = editing;
        Lay();
    }

    /// <summary>The slot the placeholder fills, for its ladder.</summary>
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
    }

    /// <summary>
    /// The ladder: the button always (its label dropped on a narrow tile), the glyph above it from a
    /// Regular height, and the drag hint under it in edit mode when there is a line to spare.
    /// </summary>
    private void Lay()
    {
        var measured = _area.IsMeasured;
        var tallEnough = !measured || _area.HeightClass >= SizeClass.Regular;

        _addText.Visibility = !measured || _area.WidthClass >= SizeClass.Regular ? Visibility.Visible : Visibility.Collapsed;
        _icon.Visibility = tallEnough ? Visibility.Visible : Visibility.Collapsed;
        _hint.Visibility = _editing && (tallEnough || _area.Height >= 96) ? Visibility.Visible : Visibility.Collapsed;
    }
}
