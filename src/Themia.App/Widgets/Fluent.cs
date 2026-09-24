using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Shell;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The Windows 11 design language, as the few values and helpers a widget actually needs.
/// <para>
/// This exists because the port had inherited the original web app's look — arbitrary font sizes,
/// arbitrary opacities standing in for text colours, 6px and 7px paddings, a divider between every
/// row — and the result read as a web dashboard that happened to be running on Windows. Fluent is
/// specific: a type ramp, a 4px spacing grid, two corner radii, named text and fill colours, and a
/// list row of a known height. Matching it is mostly a matter of stopping inventing values.
/// </para>
/// </summary>
internal static class Fluent
{
    // ---- spacing ---------------------------------------------------------------------------

    /// <summary>The 4px grid Fluent lays out on. Every gap a widget uses is one of these.</summary>
    public const double Space1 = 4;

    public const double Space2 = 8;

    public const double Space3 = 12;

    /// <summary>The standard content inset for a card.</summary>
    public const double Space4 = 16;

    public const double Space5 = 20;

    public const double Space6 = 24;

    // ---- shape -----------------------------------------------------------------------------

    /// <summary>Fluent's control radius. Buttons, text fields, row highlights.</summary>
    public const double ControlRadius = 4;

    /// <summary>Fluent's overlay radius. Cards, flyouts, dialogs.</summary>
    public const double OverlayRadius = 8;

    /// <summary>
    /// The height of a standard Windows 11 list row, and of the compact variant a small tile uses.
    /// <para>
    /// Rows are not sized to a fraction of the tile: a list whose rows grow with the window is a web
    /// idea, and next to Explorer or Settings it reads as the wrong application. A tile shows as many
    /// rows of the right height as it has room for.
    /// </para>
    /// </summary>
    public const double RowHeight = 40;

    public const double RowHeightCompact = 32;

    // ---- type ------------------------------------------------------------------------------

    /// <summary>Apply a ramp step to a text block: size, line height and weight together.</summary>
    public static void Apply(this TextBlock text, TypeStep step)
    {
        text.FontSize = step.FontSize;
        text.LineHeight = step.LineHeight;
        text.FontWeight = step.SemiBold ? FontWeights.SemiBold : FontWeights.Normal;
    }

    /// <summary>A text block on a ramp step, in one of the named text colours.</summary>
    public static TextBlock Text(TypeStep step, string colorKey = "TextFillColorPrimaryBrush")
    {
        var text = new TextBlock
        {
            Foreground = FluentLayout.Themed(colorKey, Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };

        text.Apply(step);
        return text;
    }

    /// <summary>
    /// A widget's header: a 16px icon and a Body Strong title in the secondary text colour, the
    /// shape Windows uses at the top of a pane.
    /// </summary>
    public static Grid Header(string glyph, TextBlock title, FrameworkElement? trailing = null)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Space2, 0),
        };

        title.Apply(FluentType.BodyStrong);
        title.Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        title.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(title, 1);
        grid.Children.Add(icon);
        grid.Children.Add(title);

        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        return grid;
    }

    /// <summary>
    /// A list row, in the Windows 11 shape: a fixed height, the standard inset as its own padding so
    /// it spans the full width of the widget, and a subtle fill under the pointer.
    /// <para>
    /// No divider. Windows 11 lists separate rows with space and hover, not with a rule between every
    /// one — Settings, Explorer and the Widgets board all do it this way, and a ruled list is the
    /// single clearest tell of a web table.
    /// </para>
    /// </summary>
    public static Border Row(FrameworkElement content, bool compact = false)
    {
        var row = new Border
        {
            Child = content,
            MinHeight = compact ? RowHeightCompact : RowHeight,
            Padding = new Thickness(Space4, 0, Space4, 0),
            Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent),
        };

        content.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    /// <summary>The fill a row takes under the pointer.</summary>
    public static Brush RowHover() => FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray);

    /// <summary>The fill a row shows when it is not hovered: transparent, but a real brush.</summary>
    public static Brush RowRest() => FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
}
