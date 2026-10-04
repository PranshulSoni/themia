using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    public static void Ping(List<FrameworkElement> rows, PingConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Ping"));

        AddHostRow(rows, config, save);
        MeterRows(rows, config, save, "the host is probed");
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
    private static void AddHostRow(List<FrameworkElement> rows, PingConfig config, Action save)
    {
        var box = new TextBox
        {
            Text = config.Host,
            PlaceholderText = PingConfig.DefaultHost,
            MinWidth = 200,
        };

        var verdict = FluentLayout.Verdict();

        void Judge(bool writeThrough)
        {
            var typed = box.Text.Trim();

            if (typed.Length == 0)
            {
                FluentLayout.Confirm(verdict, $"Measuring {PingConfig.DefaultHost}");

                if (writeThrough && config.Host.Length != 0)
                {
                    config.Host = string.Empty;
                    save();
                }

                return;
            }

            if (!HostAddress.IsPlausible(typed))
            {
                FluentLayout.Refuse(verdict, "Not a host name or address");
                return;
            }

            FluentLayout.Confirm(verdict, string.Empty);

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

        rows.Add(FluentLayout.FieldRow(Glyphs.Link, "Host",
            "A name or an IP address to measure against", box, verdict));
    }
}
