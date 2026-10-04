using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Themia.Localization.Generator;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// [S-1111] Locale area fragments: <c>Locales/&lt;area&gt;/&lt;code&gt;.json</c> beside the root
/// <c>Locales/&lt;code&gt;.json</c>. The string-table generator runs here in-process on in-memory
/// files, so the build-error rules (which a real build would only show as a failed build) are
/// asserted directly. The merged result of the real files is covered by the S1105 completeness tests.
/// </summary>
public sealed class LocaleFragmentTests
{
    private const string Root = "C:/proj/Localization/Locales/";

    private sealed class MemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }

    private sealed record Result(ImmutableArray<Diagnostic> Errors, string Keys, string Tables)
    {
        public string ErrorText => string.Join("\n", Errors.Select(e => e.Id + " " + e.GetMessage()));
    }

    /// <summary>Runs the generator over (relative path under Locales/, json) pairs.</summary>
    private static Result Run(params (string Path, string Json)[] files)
    {
        var compilation = CSharpCompilation.Create("t");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new StringTableGenerator())
            .AddAdditionalTexts(files.Select(f => (AdditionalText)new MemoryText(Root + f.Path, f.Json)).ToImmutableArray());
        driver = driver.RunGenerators(compilation);
        var run = driver.GetRunResult();
        var errors = run.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        string Source(string name) => run.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith(name, StringComparison.Ordinal))?.ToString() ?? string.Empty;
        return new Result(errors, Source("Str.g.cs"), Source("StringTables.g.cs"));
    }

    [Fact]
    public void S1111_fragments_merge_into_one_table_and_english_order_is_root_then_areas_by_name()
    {
        // Fed in a scrambled order on purpose: the order must come from the names, not the input.
        var result = Run(
            ("widgets-a/en.json", """{ "wa.one": "A one" }"""),
            ("shell/de.json", """{ "shell.one": "Schale eins" }"""),
            ("de.json", """{ "common.save": "Speichern" }"""),
            ("shell/en.json", """{ "shell.one": "Shell one", "shell.two": "Shell two" }"""),
            ("en.json", """{ "common.save": "Save", "common.open": "Open" }"""),
            ("widgets-a/de.json", """{ "wa.one": "A eins" }"""));

        Assert.Empty(result.Errors);

        var order = new[] { "common.save", "common.open", "shell.one", "shell.two", "wa.one" }
            .Select(k => result.Keys.IndexOf($"<c>{k}</c>", StringComparison.Ordinal)).ToArray();
        Assert.All(order, i => Assert.True(i >= 0));
        Assert.Equal(order.OrderBy(i => i), order);

        // One German table holds the root and both fragments; a key nobody translated is null.
        var german = result.Tables[result.Tables.IndexOf("class De", StringComparison.Ordinal)..result.Tables.IndexOf("class Es", StringComparison.Ordinal)];
        Assert.Contains("\"Speichern\"", german);
        Assert.Contains("\"Schale eins\"", german);
        Assert.Contains("\"A eins\"", german);
        Assert.Contains("null", german);
        Assert.Contains("\"Shell two\"", result.Tables);
    }

    [Fact]
    public void S1111_empty_fragments_and_root_only_sets_are_fine()
    {
        Assert.Empty(Run(("en.json", """{ "a.b": "x" }""")).Errors);
        Assert.Empty(Run(("en.json", """{ "a.b": "x" }"""), ("shell/en.json", "{}"), ("shell/de.json", "{}")).Errors);
    }

    [Fact]
    public void S1111_englishOnly_is_per_file_and_merged()
    {
        var result = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/en.json", """{ "$englishOnly": ["shell.one"], "shell.one": "S" }"""));
        Assert.Empty(result.Errors);
        Assert.Contains("EnglishOnly = { 1 }", result.Tables);

        // Listing another file's key is a stray, like any other.
        var stray = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/en.json", """{ "$englishOnly": ["a.b"], "shell.one": "S" }"""));
        Assert.Contains("THEMIALOC005", stray.ErrorText);
    }

    [Fact]
    public void S1111_a_key_defined_in_two_english_fragments_is_an_error_naming_both_files()
    {
        var result = Run(
            ("en.json", """{ "common.save": "Save" }"""),
            ("shell/en.json", """{ "shell.x": "x" }"""),
            ("widgets-a/en.json", """{ "shell.x": "x again" }"""));

        var error = Assert.Single(result.Errors);
        Assert.Equal("THEMIALOC008", error.Id);
        Assert.Contains("shell.x", error.GetMessage());
        Assert.Contains("Locales/shell/en.json", error.GetMessage());
        Assert.Contains("Locales/widgets-a/en.json", error.GetMessage());
    }

    [Fact]
    public void S1111_a_key_in_the_root_file_and_a_fragment_is_a_duplicate_too()
    {
        var result = Run(
            ("en.json", """{ "common.save": "Save" }"""),
            ("shell/en.json", """{ "common.save": "Save" }"""));

        var error = Assert.Single(result.Errors);
        Assert.Equal("THEMIALOC008", error.Id);
        Assert.Contains("Locales/en.json", error.GetMessage());
        Assert.Contains("Locales/shell/en.json", error.GetMessage());
    }

    [Fact]
    public void S1111_a_key_defined_in_two_fragments_of_one_translated_language_is_an_error_naming_both_files()
    {
        // shell/de.json is a legitimate home for it (shell/en.json has it); widgets-a/de.json is not.
        var result = Run(
            ("en.json", """{ "common.save": "Save" }"""),
            ("shell/en.json", """{ "shell.x": "x" }"""),
            ("shell/de.json", """{ "shell.x": "x de" }"""),
            ("widgets-a/en.json", "{}"),
            ("widgets-a/de.json", """{ "shell.x": "x de 2" }"""));

        var error = Assert.Single(result.Errors);
        Assert.Equal("THEMIALOC008", error.Id);
        Assert.Contains("Locales/shell/de.json", error.GetMessage());
        Assert.Contains("Locales/widgets-a/de.json", error.GetMessage());
    }

    [Fact]
    public void S1111_a_fragment_for_an_unknown_language_is_an_error()
    {
        var result = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/en.json", "{}"),
            ("shell/xx.json", "{}"));

        var error = Assert.Single(result.Errors);
        Assert.Equal("THEMIALOC003", error.Id);
        Assert.Contains("Locales/shell/xx.json", error.GetMessage());
    }

    [Fact]
    public void S1111_a_fragment_key_missing_from_its_areas_english_file_is_an_error()
    {
        // Typo / stale key.
        var typo = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/en.json", """{ "shell.one": "S" }"""),
            ("shell/de.json", """{ "shell.won": "S" }"""));
        var error = Assert.Single(typo.Errors);
        Assert.Equal("THEMIALOC006", error.Id);
        Assert.Contains("Locales/shell/de.json", error.GetMessage());
        Assert.Contains("Locales/shell/en.json", error.GetMessage());

        // A key the *other* area's English file has does not count: the key lives in its own area.
        var wrongArea = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/en.json", """{ "shell.one": "S" }"""),
            ("widgets-a/en.json", "{}"),
            ("widgets-a/de.json", """{ "shell.one": "S" }"""));
        Assert.Equal("THEMIALOC006", Assert.Single(wrongArea.Errors).Id);

        // Same for a root file against the root en.json.
        var root = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/en.json", """{ "shell.one": "S" }"""),
            ("de.json", """{ "shell.one": "S" }"""));
        Assert.Equal("THEMIALOC006", Assert.Single(root.Errors).Id);
    }

    [Fact]
    public void S1111_a_translated_fragment_without_its_areas_english_file_is_an_error()
    {
        var result = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/de.json", """{ "shell.one": "S" }"""));

        var error = Assert.Single(result.Errors);
        Assert.Equal("THEMIALOC006", error.Id);
        Assert.Contains("Locales/shell/en.json", error.GetMessage());
    }

    [Fact]
    public void S1111_a_broken_area_folder_name_or_fragment_json_is_an_error()
    {
        var badName = Run(("en.json", """{ "a.b": "x" }"""), ("Widgets A/en.json", "{}"));
        Assert.Equal("THEMIALOC009", Assert.Single(badName.Errors).Id);

        var badJson = Run(("en.json", """{ "a.b": "x" }"""), ("shell/en.json", """{ "shell.one": """));
        Assert.Contains("THEMIALOC001", badJson.ErrorText);
        Assert.Contains("Locales/shell/en.json", badJson.ErrorText);
    }

    [Fact]
    public void S1111_files_that_are_not_locale_files_are_ignored_by_the_generator()
    {
        // Two levels deep, or outside Localization/Locales, is none of the generator's business.
        var result = Run(
            ("en.json", """{ "a.b": "x" }"""),
            ("shell/deep/en.json", "not json at all"));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void S1111_the_root_english_file_is_required()
    {
        var result = Run(("shell/en.json", """{ "shell.one": "S" }"""));
        Assert.Equal("THEMIALOC002", Assert.Single(result.Errors).Id);
    }
}
