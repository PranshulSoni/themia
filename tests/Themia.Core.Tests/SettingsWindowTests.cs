using Themia.Core.Settings;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The pure parts of the Windows 11-style settings window ([S-1540..S-1559]): the "Find a setting"
/// matcher, back/forward history, the colour picker's presets and the uninstall plan.
/// </summary>
public sealed class SettingsWindowTests
{
    private static SettingEntry Row(string page, string title, string? description = null, string? section = null, params string[] keywords)
        => new(page, page, section, title, description, keywords);

    private static readonly SettingEntry[] Sample =
    [
        new("Appearance", "Appearance", null, "Appearance", null, [], IsPage: true),
        Row("Appearance", "Theme", "Follows Windows unless you pick one", "Theme", "Use system setting", "Light", "Dark"),
        Row("Appearance", "Accent", "Follows the Windows accent colour when empty", "Colours"),
        Row("Appearance", "Border colour", "Uses the theme's card stroke when empty", "Shape"),
        Row("Appearance", "Widget opacity", null, "Colours"),
        Row("Accounts", "Add account", "Choose the service to sign in to", "Add an account"),
        Row("General", "Start with Windows", "Adds a per-user startup entry", "System"),
        Row("Desktop › Clock", "Time format", "12 or 24 hours", "Display", "24-hour", "12-hour"),
        Row("Layout", "Snap to grid", "Hold Alt while dragging to place freely"),
    ];

    private static List<string> Titles(string query, int limit = SettingsSearch.DefaultLimit)
        => [.. SettingsSearch.Find(Sample, query, limit).Select(m => m.Entry.Title)];

    [Fact]
    public void S1542_blank_query_suggests_nothing()
    {
        Assert.Empty(Titles(string.Empty));
        Assert.Empty(Titles("   "));
        Assert.Empty(Titles("--"));
    }

    [Fact]
    public void S1542_title_prefix_ranks_above_a_mention_elsewhere()
    {
        // "acc" starts Accent's title; Add account only has it inside a word.
        var titles = Titles("acc");
        Assert.Equal("Accent", titles[0]);
        Assert.Contains("Add account", titles);
    }

    [Fact]
    public void S1542_every_word_must_land_somewhere()
    {
        Assert.Equal(["Border colour"], Titles("border colour"));
        Assert.Empty(Titles("border kangaroo"));
    }

    [Fact]
    public void S1543_british_and_american_spellings_find_each_other()
    {
        Assert.Contains("Border colour", Titles("border color"));
        Assert.Contains("Accent", Titles("color"));
        Assert.Equal(["color"], SettingsSearch.Tokenize("Colour"));
    }

    [Fact]
    public void S1543_drop_down_options_are_keywords()
    {
        Assert.Equal("Theme", Titles("dark")[0]);
        Assert.Equal("Time format", Titles("24")[0]);
    }

    [Fact]
    public void S1543_section_page_and_description_also_match()
    {
        Assert.Contains("Widget opacity", Titles("colours"));
        Assert.Contains("Time format", Titles("clock"));
        Assert.Contains("Snap to grid", Titles("dragging"));
    }

    [Fact]
    public void S1544_a_small_typo_still_lands_on_a_title()
    {
        Assert.Contains("Appearance", Titles("apperance"));
        Assert.Contains("Theme", Titles("thme"));

        // Three letters or fewer get no typo budget, and a short word with the wrong first letter
        // is a different word.
        Assert.Empty(Titles("xcc"));
        Assert.Equal(0, SettingsSearch.TypoBudget("abc"));
        Assert.Equal(1, SettingsSearch.TypoBudget("abcde"));
        Assert.Equal(2, SettingsSearch.TypoBudget("abcdefgh"));
    }

    [Fact]
    public void S1544_the_page_itself_comes_before_its_rows()
    {
        var first = SettingsSearch.Find(Sample, "appearance")[0];
        Assert.True(first.Entry.IsPage);
    }

    [Fact]
    public void S1545_limit_and_duplicates()
    {
        Assert.Single(Titles("e", 1));

        var doubled = Sample.Concat(Sample).ToArray();
        var hits = SettingsSearch.Find(doubled, "theme");
        Assert.Single(hits, h => h.Entry.Title == "Theme");
    }

    [Fact]
    public void S1544_edit_distance_is_bounded_and_counts_transpositions()
    {
        Assert.Equal(0, SettingsSearch.EditDistance("theme", "theme", 2));
        Assert.Equal(1, SettingsSearch.EditDistance("teh", "the", 2));
        Assert.Equal(1, SettingsSearch.EditDistance("colr", "color", 2));
        Assert.Equal(3, SettingsSearch.EditDistance("a", "abcdef", 2));
    }

    // -------------------------------------------------------------------------------------------
    // Back and forward
    // -------------------------------------------------------------------------------------------

    private sealed record Page(string Name);

    [Fact]
    public void S1547_back_and_forward_step_through_navigations()
    {
        var history = new NavHistory<Page>();
        Assert.Null(history.Current);
        Assert.False(history.CanBack);

        history.Push(new Page("General"));
        history.Push(new Page("Appearance"));
        history.Push(new Page("About"));

        Assert.Equal(new Page("Appearance"), history.Back());
        Assert.Equal(new Page("General"), history.Back());
        Assert.Null(history.Back());
        Assert.Equal(new Page("General"), history.Current);
        Assert.Equal(new Page("Appearance"), history.Forward());
        Assert.True(history.CanForward);
    }

    [Fact]
    public void S1547_pushing_from_an_older_entry_drops_the_forward_tail_and_repeats_coalesce()
    {
        var history = new NavHistory<Page>();
        history.Push(new Page("A"));
        history.Push(new Page("B"));
        history.Push(new Page("B"));
        history.Push(new Page("C"));
        history.Back();
        history.Push(new Page("D"));

        Assert.False(history.CanForward);
        Assert.Equal(new Page("B"), history.Back());
        Assert.Equal(new Page("A"), history.Back());
        Assert.False(history.CanBack);
    }

    [Fact]
    public void S1547_removed_pages_leave_the_history()
    {
        var history = new NavHistory<Page>();
        history.Push(new Page("A"));
        history.Push(new Page("Widget"));
        history.Push(new Page("A"));
        history.Push(new Page("C"));

        history.RemoveWhere(p => p.Name == "Widget");

        // A, A merged into one; standing on C still.
        Assert.Equal(new Page("C"), history.Current);
        Assert.Equal(new Page("A"), history.Back());
        Assert.False(history.CanBack);

        // The current page itself removed: stand on the survivor before it.
        history.Forward();
        history.RemoveWhere(p => p.Name == "C");
        Assert.Equal(new Page("A"), history.Current);
        Assert.False(history.CanForward);

        history.RemoveWhere(_ => true);
        Assert.Null(history.Current);
    }

    // -------------------------------------------------------------------------------------------
    // Colour presets
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void S1549_the_windows_palette_is_48_distinct_colours_in_rows_of_8()
    {
        Assert.Equal(48, ColorPresets.Windows.Count);
        Assert.Equal(0, ColorPresets.Windows.Count % ColorPresets.Columns);
        Assert.Equal(ColorPresets.Columns, ColorPresets.Neutrals.Count);

        foreach (var preset in ColorPresets.Surfaces)
        {
            Assert.Equal(preset.Hex, HexColor.Normalise(preset.Hex));
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
        }

        Assert.Equal(ColorPresets.Surfaces.Count, ColorPresets.Surfaces.Select(p => p.Hex).Distinct().Count());
    }

    [Fact]
    public void S1549_a_stored_value_finds_its_preset_whatever_its_case()
    {
        Assert.Equal("Default blue", ColorPresets.Find(ColorPresets.Windows, "#0078d4")?.Name);
        Assert.Equal("Default blue", ColorPresets.Find(ColorPresets.Windows, "0078D4")?.Name);
        Assert.Null(ColorPresets.Find(ColorPresets.Windows, "#123456"));
        Assert.Null(ColorPresets.Find(ColorPresets.Windows, string.Empty));
        Assert.Null(ColorPresets.Find(ColorPresets.Windows, "not a colour"));
    }

    // -------------------------------------------------------------------------------------------
    // Uninstall
    // -------------------------------------------------------------------------------------------

    private const string Roaming = @"C:\Users\someone\AppData\Roaming";
    private const string Local = @"C:\Users\someone\AppData\Local";

    [Fact]
    public void S1552_route_follows_packaging()
    {
        var packaged = UninstallPlan.Create(packaged: true, deleteData: false, Roaming, Local, "Themia", "1.2.0");
        Assert.Equal(UninstallRoute.RemovePackage, packaged.Route);
        Assert.Equal(UninstallPlan.OwnAppPage, packaged.Fallback);

        var loose = UninstallPlan.Create(packaged: false, deleteData: false, Roaming, Local, "Themia", "1.2.0");
        Assert.Equal(UninstallRoute.OpenInstalledApps, loose.Route);
        Assert.Equal(UninstallPlan.InstalledApps, loose.Fallback);
    }

    [Fact]
    public void S1553_nothing_is_deleted_unless_asked()
    {
        var plan = UninstallPlan.Create(packaged: false, deleteData: false, Roaming, Local, "Themia", "1.0.0");
        Assert.Empty(plan.FoldersToDelete);
    }

    [Fact]
    public void S1553_asked_deletes_exactly_the_two_app_folders()
    {
        var plan = UninstallPlan.Create(packaged: false, deleteData: true, Roaming, Local, "Themia", "1.0.0");
        Assert.Equal([$@"{Roaming}\Themia", $@"{Local}\Themia"], plan.FoldersToDelete);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"Themia\..")]
    [InlineData(@"..\Roaming")]
    [InlineData("Themia/x")]
    [InlineData(" Themia")]
    public void S1553_a_suspicious_folder_name_deletes_nothing(string name)
        => Assert.Empty(UninstallPlan.DataFolders(Roaming, Local, name));

    [Fact]
    public void S1553_a_missing_or_relative_root_is_skipped()
    {
        Assert.Equal([$@"{Local}\Themia"], UninstallPlan.DataFolders(null, Local, "Themia"));
        Assert.Equal([$@"{Local}\Themia"], UninstallPlan.DataFolders(@"relative\path", Local, "Themia"));
        Assert.Empty(UninstallPlan.DataFolders(string.Empty, "  ", "Themia"));

        // The same root twice is one folder.
        Assert.Single(UninstallPlan.DataFolders(Roaming, Roaming + @"\", "Themia"));
    }

    [Fact]
    public void S1554_the_survey_carries_the_version_escaped()
    {
        Assert.Equal("https://themia.app/goodbye?v=1.2.0", UninstallPlan.SurveyUrl("1.2.0").AbsoluteUri);
        Assert.Equal("https://themia.app/goodbye?v=1.0.0-beta%2B1", UninstallPlan.SurveyUrl("1.0.0-beta+1").AbsoluteUri);
        Assert.Equal("https://themia.app/goodbye?v=unknown", UninstallPlan.SurveyUrl(null).AbsoluteUri);
    }
}
