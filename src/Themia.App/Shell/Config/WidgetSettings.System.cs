using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Themia.App.Shell;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the widgets that read the machine itself: the process list and the ping meter.
/// <para>
/// Same shape as the rest of <see cref="WidgetSettings"/> — each control writes straight to the
/// live config and saves, because the widget is on the desktop behind the window and watching it
/// change is the point.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Processes(List<FrameworkElement> rows, ProcessesConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Processes"));

        rows.Add(FluentLayout.Row(Glyphs.Sort, "Rank by", "Which column the list is ordered on",
            FluentLayout.Choice(
                [(ProcessSort.Cpu, "Processor"), (ProcessSort.Memory, "Memory")],
                config.Sort,
                value => { config.Sort = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Rows",
            "A short widget shows as many of these as it can render legibly",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 20, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Show memory", "A second column beside the processor share",
            FluentLayout.Toggle(config.ShowMemory, value => { config.ShowMemory = value; save(); })));
    }

    public static void Ping(List<FrameworkElement> rows, PingConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Ping"));
        rows.Add(FluentLayout.Row(Glyphs.Link, "Host", "A name or an IP address to measure against", HostBox(config, save)));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Every", "Seconds between probes",
            FluentLayout.Slider(config.IntervalSeconds, 1, 300, 1, "s",
                value => { config.IntervalSeconds = (int)Math.Round(value); save(); })));

        AddGraphToggle(rows, config, save);
    }

    /// <summary>
    /// The host field, with its own verdict underneath it.
    /// <para>
    /// This is the trust boundary: whatever is typed here is persisted and later handed to a name
    /// resolver, so it is checked before it is stored rather than after it is read back. A value
    /// that is not a plausible host is <b>not saved at all</b> — the widget keeps measuring the last
    /// host that was — because silently storing it would leave the box looking accepted while the
    /// tile said "unreachable", which reads as a network fault rather than as a typo.
    /// </para>
    /// <para>
    /// An empty box is not a rejection and it is not "probe nothing" either: it means the default
    /// resolver, and the line under the box says which one, so the widget's behaviour is never
    /// something the user has to infer from a blank field.
    /// </para>
    /// </summary>
    private static FrameworkElement HostBox(PingConfig config, Action save)
    {
        var box = new TextBox
        {
            Text = config.Host,
            PlaceholderText = PingConfig.DefaultHost,
            MinWidth = 200,
        };

        var verdict = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            MaxWidth = 200,
        };

        void Judge(bool writeThrough)
        {
            var typed = box.Text.Trim();

            if (typed.Length == 0)
            {
                verdict.Text = $"Measuring {PingConfig.DefaultHost}";
                verdict.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);

                if (writeThrough && config.Host.Length != 0)
                {
                    config.Host = string.Empty;
                    save();
                }

                return;
            }

            if (!HostAddress.IsPlausible(typed))
            {
                verdict.Text = "Not a host name or address";
                verdict.Foreground = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);
                return;
            }

            verdict.Text = string.Empty;

            if (writeThrough && config.Host != typed)
            {
                config.Host = typed;
                save();
            }
        }

        // Judged on every keystroke so a typo is flagged while it is still being made, and written
        // through on the same keystroke so the widget behind the window follows along. A rejected
        // value simply never reaches the config.
        box.TextChanged += (_, _) => Judge(writeThrough: true);

        // State the verdict for the value that is already stored, without saving it back — an
        // untouched field must not be able to rewrite the config just by being shown.
        Judge(writeThrough: false);

        return new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { box, verdict },
        };
    }

}
