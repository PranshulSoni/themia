using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>[S-863] A group's own rows: its icon and the area's fill. Its members follow (<see cref="ContainerChildrenRows"/>).</summary>
internal static partial class WidgetSettings
{
    /// <summary>
    /// The icon and the fill. Collapse is not mirrored here: the title bar owns it, and a second
    /// control writing the same field would be a second path to one thing. The title is the
    /// widget's own (the shared Title row), and "None" under Background makes the area see-through.
    /// </summary>
    public static void Group(List<FrameworkElement> rows, GroupConfig config, Action save)
    {
        rows.Add(FluentLayout.Row(Glyphs.Group, "Icon", "Shown in the title bar",
            IconChoice.Create(config.Icon, Glyphs.Group, glyph =>
            {
                config.Icon = glyph;
                save();
            })));

        rows.Add(FluentLayout.ColorPickerRow(Glyphs.Palette, "Fill", "The area behind the widgets in it",
            config.Fill,
            value =>
            {
                config.Fill = value;
                save();
            },
            new ColorChoiceOptions { DefaultLabel = "Theme wash" }));

        rows.Add(FluentLayout.Row(Glyphs.Surface, "Fill opacity", "How strongly a fill of your own shows",
            FluentLayout.Slider(config.FillOpacity * 100, 0, 100, 1, "%", value =>
            {
                config.FillOpacity = Math.Clamp(value / 100, 0, 1);
                save();
            })));
    }
}
