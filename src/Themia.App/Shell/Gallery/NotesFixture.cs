#if DEBUG
using Themia.App.Services;
using Themia.App.Services.Notes;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Notes;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1368] Notes for the debug gallery, in a <b>throwaway store</b>: a fresh folder under the temp
/// directory for every page, never the user's notes. The plain page has an empty store (a preview
/// card mints no note, so it shows the empty editor); the <c>+fixture</c> page four notes, one of
/// them a markdown sampler that exercises headings, emphasis, lists, tasks, a quote and code.
/// </summary>
internal static class NotesFixture
{
    private const string Scope = "gallery";

    private static int _generation;

    private static string? _shown;

    private const string Sampler =
        "# Weekend\n" +
        "Call the **landlord** about the boiler, *before* Friday.\n" +
        "\n" +
        "## To do\n" +
        "- [x] Book dentist (Tue or Thu)\n" +
        "- [ ] Renew passport before March\n" +
        "- [ ] Ask Anna about ~~Zurich~~ Lucerne\n" +
        "\n" +
        "1. Pack the `charger`\n" +
        "2. Water the plants\n" +
        "\n" +
        "> The best time to plant a tree was twenty years ago.\n" +
        "\n" +
        "```\n" +
        "door code 4711\n" +
        "```\n" +
        "Ideas: weekend in Lucerne, train at 08:12, hotel near the lake, and a boat trip if the weather holds.";

    public static void Install(bool populated)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Themia.Gallery", $"notes-{Environment.ProcessId}-{++_generation}");

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(Scope, "could not clear the gallery's note folder; it starts with what is there", ex);
        }

        var pool = new NotePool(new NoteStore(directory));
        _shown = null;

        if (populated)
        {
            pool.Create("Groceries", "- [ ] Milk\n- [ ] Bread\n- [x] Coffee");
            pool.Create("Meeting notes", "## Thursday\nShip the **beta** on the 14th.");
            pool.Create("Reading list", "- *The Overstory*\n- *Piranesi*");
            _shown = pool.Create("Weekend", Sampler);
        }

        AppServices.Notes = pool;
    }

    public static NotesConfig Config() => new() { NoteId = _shown };
}
#endif
