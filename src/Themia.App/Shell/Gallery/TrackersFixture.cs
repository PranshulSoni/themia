#if DEBUG
namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1443] A made-up clipboard history for the debug gallery's <c>Clipboard+fixture</c> page. The
/// history is in memory per widget and never comes from a config, so the fixture is a seed the view
/// reads on attach. The plain page has none and shows the real empty state. Nothing here is ever
/// read from, or written to, the real clipboard.
/// </summary>
internal static class ClipboardFixture
{
    /// <summary>Entries oldest first (each added on top of the last), or null for none.</summary>
    public static IReadOnlyList<(string Text, TimeSpan Ago)>? Entries { get; private set; }

    public static void Install(bool populated) => Entries = populated
        ?
        [
            ("Meeting notes:\n- ship the beta on Friday\n- ask Anna about the icons\n- book the room for the retro", TimeSpan.FromHours(26)),
            ("+41 44 555 01 23", TimeSpan.FromHours(5)),
            ("#60cdff", TimeSpan.FromHours(3)),
            ("anna.keller@example.com", TimeSpan.FromMinutes(95)),
            ("1,234.50", TimeSpan.FromMinutes(52)),
            ("Bahnhofstrasse 1\n8001 Zurich\nSwitzerland", TimeSpan.FromMinutes(31)),
            ("The quick brown fox jumps over the lazy dog, and then keeps on running far past the edge of any single line this widget could draw.", TimeSpan.FromMinutes(12)),
            ("https://learn.microsoft.com/windows/apps/design/", TimeSpan.FromMinutes(4)),
            ("git log --oneline --graph", TimeSpan.FromMinutes(1)),
        ]
        : null;
}
#endif
