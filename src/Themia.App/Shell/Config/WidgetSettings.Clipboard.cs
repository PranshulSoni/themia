using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Clipboard widget's settings rows.
/// <para>
/// The most important thing on this page is a sentence rather than a control: it says, in plain
/// words, that the history is kept in memory and never written to disk. There is deliberately no
/// switch to change that. A clipboard history captures whatever was copied — which regularly
/// includes a password or a recovery code — and a "save history" toggle, however it is defaulted,
/// is an offer to create a credentials file.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Clipboard(List<FrameworkElement> rows, ClipboardConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Clipboard"));

        rows.Add(new TextBlock
        {
            Text = "This widget keeps copied text in memory only, for as long as the app is running. "
                + "Nothing is written to disk, and copied files and images are never read. "
                + "Closing Themia forgets the lot.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray),
            Margin = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space2),
        });

        // The one setting here that genuinely changes exposure, so it says so: fewer entries kept
        // is less sitting in memory.
        rows.Add(FluentLayout.Row(Glyphs.Count, "Entries",
            "How much history to keep. Lowering it forgets the oldest straight away",
            FluentLayout.Slider(
                config.EffectiveCapacity,
                ClipboardHistory.MinCapacity,
                ClipboardHistory.MaxCapacity,
                5,
                string.Empty,
                value => { config.Capacity = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Show when it was copied",
            "Hidden automatically when the widget is too narrow",
            FluentLayout.Toggle(config.ShowTimestamp, value => { config.ShowTimestamp = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Delete, "Clear history",
            "Use the bin button on the widget itself; it asks first", new TextBlock
            {
                Text = "On the widget",
                FontSize = 12,
                Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray),
            }));
    }
}
