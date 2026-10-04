using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public sealed class AppNamesTests
{
    [Fact]
    public void S458_sanitize_cuts_at_the_first_control_character()
    {
        // The real-world Spotify case: version-info bytes leaked past the string's end.
        Assert.Equal("Spotify", AppNames.Sanitize("Spotify\u00008\u0016\u0001FileV"));
        Assert.Equal("Google Chrome", AppNames.Sanitize("Google Chrome"));
        Assert.Equal("Padded", AppNames.Sanitize("  Padded  "));
        Assert.Null(AppNames.Sanitize("\u0000garbage"));
        Assert.Null(AppNames.Sanitize(string.Empty));
        Assert.Null(AppNames.Sanitize(null));
    }

    [Fact]
    public void S458_entry_point_identifiers_read_as_the_app()
    {
        Assert.Equal("WhatsApp", AppNames.Sanitize("WhatsApp.Root"));
        Assert.Equal("Teams", AppNames.StripEntrypointSuffix("Teams.exe"));
        Assert.Equal("Foo", AppNames.StripEntrypointSuffix("Foo.APP"));

        // Real names keep their dots.
        Assert.Equal("Node.js JavaScript Runtime", AppNames.StripEntrypointSuffix("Node.js JavaScript Runtime"));
        Assert.Equal("Microsoft.Todos", AppNames.StripEntrypointSuffix("Microsoft.Todos"));
        Assert.Equal(".Root", AppNames.StripEntrypointSuffix(".Root"));
    }

    [Fact]
    public void S458_only_identifier_shaped_names_prefer_the_product_name()
    {
        Assert.True(AppNames.IsIdentifierLike("Microsoft.Todos"));
        Assert.False(AppNames.IsIdentifierLike("WhatsApp"));
        Assert.False(AppNames.IsIdentifierLike("Node.js JavaScript Runtime"));

        Assert.Equal("Microsoft To Do", AppNames.FromVersionInfo("Microsoft.Todos", "Microsoft To Do"));
        Assert.Equal("Google Chrome", AppNames.FromVersionInfo("Google Chrome", "Chrome"));
        Assert.Equal("Microsoft.Todos", AppNames.FromVersionInfo("Microsoft.Todos", "Also.Identifier"));
        Assert.Equal("Product", AppNames.FromVersionInfo(null, "Product"));
        Assert.Null(AppNames.FromVersionInfo(null, null));
    }

    [Fact]
    public void S458_exe_stems_and_prettified_keys()
    {
        Assert.Equal("chrome", AppNames.ExeStem(@"C:\x\chrome.exe"));
        Assert.Equal("vlc", AppNames.ExeStem("C:/x/vlc.exe"));
        Assert.Equal(".hidden", AppNames.ExeStem(@"C:\x\.hidden"));
        Assert.Null(AppNames.ExeStem(" "));
        Assert.Equal("Githubdesktop", AppNames.Prettify("githubdesktop"));
    }
}
