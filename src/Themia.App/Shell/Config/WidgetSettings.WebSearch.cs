using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Search widget's settings rows.
/// <para>
/// The template box is the trust boundary on this page. Everything else picks from a closed set of
/// engines and cannot be wrong; the template composes a URL that the desktop will open, so it is
/// checked by <see cref="SearchEngines.CheckTemplate"/> and the reason it was refused is shown
/// under the box rather than swallowed. A refused template is never corrected into a working one.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private static readonly IReadOnlyList<(SearchEngine Value, string Label)> EngineChoices =
        [.. SearchEngines.All.Select(engine => (engine, SearchEngines.Label(engine)))];

    public static void WebSearch(List<FrameworkElement> rows, WebSearchConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Search"));

        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, 0),
        };

        var box = new TextBox
        {
            // Read once to seed the control, then never mirrored: the config stays the only copy of
            // the value, and the box writes to it rather than shadowing it.
            Text = config.CustomTemplate,
            Width = 260,
            PlaceholderText = "https://example.com/search?q=%s",
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
        };

        var templateRow = FluentLayout.Row(Glyphs.Link, "Search address",
            "Your own search, with %s where the words go", box);

        void ShowStatus()
        {
            var visible = config.EffectiveEngine == SearchEngine.Custom;

            templateRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            status.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (!visible)
            {
                return;
            }

            var problem = SearchEngines.CheckTemplate(config.CustomTemplate);

            if (problem == SearchProblem.None)
            {
                // Confirming is worth as much as refusing: a template that looks accepted but
                // searches somewhere else is the failure this check exists to prevent.
                var example = SearchEngines.Build(SearchEngine.Custom, config.CustomTemplate, "hello world");

                status.Text = example.IsValid ? $"Searches {example.Url}" : SearchEngines.ProblemMessage(example.Problem);
                status.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
                return;
            }

            status.Text = SearchEngines.ProblemMessage(problem);
            status.Foreground = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.Red);
        }

        void Commit()
        {
            var typed = box.Text.Trim();

            // Stored whether or not it is usable, and the box keeps what was typed: a template is
            // corrected by editing it, not by having it wiped on the first wrong character.
            if (typed != config.CustomTemplate)
            {
                config.CustomTemplate = typed;
                save();
            }

            ShowStatus();
        }

        // On commit, not per keystroke: a URL is only ever valid once it has been fully typed, so
        // validating each character would spend the whole time saying it is wrong.
        box.LostFocus += (_, _) => Commit();

        box.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Search, "Engine", "Where a query is sent",
            FluentLayout.Choice(EngineChoices, config.EffectiveEngine, value =>
            {
                config.Engine = value;
                save();
                ShowStatus();
            })));

        rows.Add(templateRow);
        rows.Add(status);

        rows.Add(FluentLayout.Row(Glyphs.Clear, "Empty the box after searching", null,
            FluentLayout.Toggle(config.ClearAfterSearch, value => { config.ClearAfterSearch = value; save(); })));

        ShowStatus();
    }
}
