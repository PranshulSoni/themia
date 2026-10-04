using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Localization;
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
    private static IReadOnlyList<(SearchEngine Value, string Label)> EngineChoices()
        => [.. SearchEngines.All.Select(engine => (engine, SearchEngines.Label(engine)))];

    public static void WebSearch(List<FrameworkElement> rows, WebSearchConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.Common_Search.Text()));

        var verdict = FluentLayout.Verdict();

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

        var templateRow = FluentLayout.FieldRow(Glyphs.Link, Str.WidgetsC_WebSearch_Settings_Address_Label.Text(),
            Str.WidgetsC_WebSearch_Settings_Address_Desc.Text(), box, verdict);

        void ShowVerdict()
        {
            // The whole row goes when the engine is one of the built-in ones: there is no template
            // to judge, so there is nothing for the row to say.
            var custom = config.EffectiveEngine == SearchEngine.Custom;
            templateRow.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

            if (!custom)
            {
                return;
            }

            var problem = SearchEngines.CheckTemplate(config.CustomTemplate);

            if (problem != SearchProblem.None)
            {
                FluentLayout.Refuse(verdict, SearchEngines.ProblemMessage(problem));
                return;
            }

            // Confirming is worth as much as refusing: a template that looks accepted but searches
            // somewhere else is the failure this check exists to prevent.
            var example = SearchEngines.Build(SearchEngine.Custom, config.CustomTemplate, "hello world");

            if (example.IsValid)
            {
                FluentLayout.Confirm(verdict, Str.WidgetsC_WebSearch_Settings_Searches.Format(("url", example.Url)));
            }
            else
            {
                FluentLayout.Refuse(verdict, SearchEngines.ProblemMessage(example.Problem));
            }
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

            ShowVerdict();
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

        rows.Add(FluentLayout.Row(Glyphs.Search, Str.WebSearch_Engine_Label.Text(), Str.WebSearch_Engine_Desc.Text(),
            FluentLayout.Choice(EngineChoices(), config.EffectiveEngine, value =>
            {
                config.Engine = value;
                save();
                ShowVerdict();
            })));

        rows.Add(templateRow);

        rows.Add(FluentLayout.Header(Str.WidgetsC_WebSearch_Settings_Behaviour.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Clear, Str.WidgetsC_WebSearch_Settings_Clear_Label.Text(),
            Str.WidgetsC_WebSearch_Settings_Clear_Desc.Text(),
            FluentLayout.Toggle(config.ClearAfterSearch, value => { config.ClearAfterSearch = value; save(); })));

        ShowVerdict();
    }
}
