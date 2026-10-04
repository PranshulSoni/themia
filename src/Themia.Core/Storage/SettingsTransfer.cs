using System.Globalization;
using System.Text.Json;
using Themia.Core.Diagnostics;
using Themia.Core.Model;

namespace Themia.Core.Storage;

/// <summary>What an import would do, in the user's words, shown before anything is changed.</summary>
/// <param name="Changes">One line per thing that would be different. Empty when the file matches.</param>
/// <param name="Notes">What the user should know that is not a change: repairs, kept settings.</param>
public sealed record ImportPreview(IReadOnlyList<string> Changes, IReadOnlyList<string> Notes);

/// <summary>
/// Exporting the whole settings document to a file and importing one back. [S-494..S-498]
/// <para>
/// The file is the state document itself, byte for byte what <c>state.json</c> holds, so there is
/// one format and one reader: an import goes through <see cref="AppStateStore.Parse"/>, the same
/// trust boundary the app's own file crosses at startup. A file that would be refused at startup is
/// refused here, before it can replace anything.
/// </para>
/// </summary>
public static class SettingsTransfer
{
    private const string Scope = "transfer";

    /// <summary>The document as it is exported: the same JSON the store writes.</summary>
    public static string Serialize(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, AppStateJsonContext.Default.AppState);
    }

    /// <summary>
    /// [S-494] Write a document to <paramref name="path"/>, atomically: a sibling temp file moved
    /// over the target, so an export interrupted half way never leaves a truncated file that a later
    /// import would refuse. Returns null on success, otherwise what went wrong in the user's words.
    /// </summary>
    public static string? WriteFile(AppState state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var temp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(temp, Serialize(state));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Error(Scope, $"could not write settings to {path}", ex);

            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException cleanup)
            {
                Log.Warn(Scope, "could not remove the temp settings file", cleanup);
            }
            catch (UnauthorizedAccessException cleanup)
            {
                Log.Warn(Scope, "could not remove the temp settings file", cleanup);
            }

            return ex is UnauthorizedAccessException
                ? "Themia isn't allowed to write there."
                : $"The file couldn't be written: {ex.Message}";
        }
    }

    /// <summary>
    /// [S-495] Read a file the user picked to import. A missing or unreadable file and a document the
    /// store would refuse both come back as a refusal with a reason; nothing is ever half-read.
    /// </summary>
    public static ParseResult ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException ex)
        {
            return ParseResult.Refused(ParseRefusal.Unreadable, "The file isn't there any more.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ParseResult.Refused(ParseRefusal.Unreadable, $"The file couldn't be read: {ex.Message}", ex);
        }

        return Read(json, path);
    }

    /// <summary>
    /// [S-495] The content half of <see cref="ReadFile"/>: a document the store would refuse is
    /// refused, and so is one with no views in it.
    /// <para>
    /// The store repairs a document with no views by seeding an empty one, which is right for the
    /// app's own file — a half-valid layout is still the user's layout — and wrong for an import:
    /// any JSON object at all, another app's config included, would be accepted as "settings with
    /// one blank view" and replace every view the user has.
    /// </para>
    /// </summary>
    public static ParseResult Read(string json, string source)
    {
        if (!string.IsNullOrWhiteSpace(json) && !HasViews(json))
        {
            Log.Warn(Scope, $"refused an import from {source}: no views in it");
            return ParseResult.Refused(ParseRefusal.NotState, Explain(ParseRefusal.NotState));
        }

        var parsed = AppStateStore.Parse(json);
        if (parsed.State is null)
        {
            Log.Warn(Scope, $"refused an import from {source}: {parsed.Problem}");
            return ParseResult.Refused(parsed.Refusal, Explain(parsed.Refusal), parsed.Error);
        }

        return parsed;
    }

    /// <summary>Whether the document's top level carries a non-empty <c>views</c> array.</summary>
    private static bool HasViews(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("views", out var views)
                && views.ValueKind == JsonValueKind.Array
                && views.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            // ignore: not JSON at all; the store's own parse says so in its own words
            return true;
        }
    }

    /// <summary>The store's reason, said the way the import dialog says it.</summary>
    public static string Explain(ParseRefusal refusal) => refusal switch
    {
        ParseRefusal.Empty => "The file is empty.",
        ParseRefusal.NotJson => "This isn't a Themia settings file: it isn't valid JSON.",
        ParseRefusal.NotState => "This isn't a Themia settings file.",
        ParseRefusal.NewerSchema => "These settings come from a newer version of Themia. Update Themia, then import them.",
        _ => "The file couldn't be read as Themia settings.",
    };

    /// <summary>The occasion an import's backup is named for: <c>state.before-import-….json</c>.</summary>
    public const string ImportOccasion = "import";

    /// <summary>[S-892] The occasion a reset's backup is named for: <c>state.before-reset-….json</c>.</summary>
    public const string ResetOccasion = "reset";

    /// <summary>
    /// [S-497] Where the settings the user has now are kept when an import (or [S-892] a reset)
    /// replaces them: beside the state file, named for the occasion and the moment, never
    /// overwriting an earlier one.
    /// </summary>
    public static string BackupPath(string statePath, DateTime now, string occasion = ImportOccasion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(occasion);

        var dir = Path.GetDirectoryName(statePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(statePath);
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(dir, $"{name}.before-{occasion}-{stamp}.json");
    }

    /// <summary>
    /// [S-498] Replace <paramref name="target"/>'s contents with <paramref name="incoming"/>'s, in
    /// place, so everything holding the live state sees the new one.
    /// <para>
    /// Start with Windows is the exception: it is a fact about this PC's registry rather than a
    /// preference, and importing another machine's answer would leave the setting saying something
    /// the registry does not.
    /// </para>
    /// </summary>
    public static void Apply(AppState target, AppState incoming)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(incoming);

        var runAtStartup = target.Design.RunAtStartup;

        target.SchemaVersion = incoming.SchemaVersion;
        target.Views = incoming.Views;
        target.ActiveViewId = incoming.ActiveViewId;
        target.MonitorViews = incoming.MonitorViews;
        target.Design = incoming.Design;
        target.Design.RunAtStartup = runAtStartup;
        target.Preferences = incoming.Preferences;
        target.DefaultViewForNewDisplays = incoming.DefaultViewForNewDisplays;
        target.DisabledMonitors = incoming.DisabledMonitors;
        target.MonitorScales = incoming.MonitorScales;
        target.ViewBars = incoming.ViewBars;
    }

    /// <summary>
    /// [S-496] Say what an import would change, before it does.
    /// <para>
    /// Views are matched by id, then by name: a file exported from this install keeps its ids, and
    /// one from another PC has its own, where the name is the only thing the two have in common.
    /// Widgets are matched by id within a matched view and compared as their serialised form, so
    /// any difference at all — position, size, a setting — counts as a change.
    /// </para>
    /// </summary>
    /// <param name="repairs">What reading the file had to repair, from <see cref="ParseResult.Repairs"/>.</param>
    /// <param name="connectedDisplays">Device names of the displays attached now.</param>
    public static ImportPreview Compare(
        AppState current,
        AppState incoming,
        IReadOnlyList<string> repairs,
        IReadOnlyCollection<string> connectedDisplays)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(repairs);
        ArgumentNullException.ThrowIfNull(connectedDisplays);

        var changes = new List<string>();
        CompareViews(current, incoming, changes);
        CompareDesign(current.Design, incoming.Design, changes);
        ComparePreferences(current.Preferences, incoming.Preferences, changes);

        var notes = new List<string>();

        var elsewhere = incoming.MonitorViews.Keys.Count(id => !connectedDisplays.Contains(id));
        if (elsewhere > 0)
        {
            notes.Add(elsewhere == 1
                ? "One display named in the file isn't connected. Its view is kept, and comes back when it is."
                : $"{elsewhere} displays named in the file aren't connected. Their views are kept, and come back when they are.");
        }

        if (repairs.Count > 0)
        {
            notes.Add(repairs.Count == 1
                ? $"One thing in the file had to be repaired to be read: {repairs[0]}."
                : $"{repairs.Count} things in the file had to be repaired to be read.");
        }

        if (current.Design.RunAtStartup != incoming.Design.RunAtStartup)
        {
            notes.Add("Start with Windows stays as it is on this PC.");
        }

        return new ImportPreview(changes, notes);
    }

    private static void CompareViews(AppState current, AppState incoming, List<string> changes)
    {
        var unmatched = new List<ViewState>(current.Views);
        var pairs = new List<(ViewState Old, ViewState New)>();
        var added = new List<ViewState>();

        // Ids first across the board, then names for what is left, so a renamed view in a file
        // from this install is a rename rather than a removal and an unrelated addition.
        var pending = new List<ViewState>();
        foreach (var view in incoming.Views)
        {
            var match = unmatched.Find(v => string.Equals(v.Id, view.Id, StringComparison.Ordinal));
            if (match is null)
            {
                pending.Add(view);
                continue;
            }

            unmatched.Remove(match);
            pairs.Add((match, view));
        }

        foreach (var view in pending)
        {
            var match = unmatched.Find(v => string.Equals(v.Name.Trim(), view.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                added.Add(view);
                continue;
            }

            unmatched.Remove(match);
            pairs.Add((match, view));
        }

        foreach (var (old, @new) in pairs)
        {
            if (!string.Equals(old.Name, @new.Name, StringComparison.Ordinal))
            {
                changes.Add($"Renames the view “{old.Name}” to “{@new.Name}”");
            }

            var detail = CompareWidgets(old, @new);
            if (detail is not null)
            {
                changes.Add($"“{@new.Name}”: {detail}");
            }
        }

        foreach (var view in added)
        {
            changes.Add($"Adds the view “{view.Name}” with {Count(view.Widgets.Count, "widget")}");
        }

        foreach (var view in unmatched)
        {
            changes.Add(view.Widgets.Count == 0
                ? $"Removes the view “{view.Name}”"
                : $"Removes the view “{view.Name}” and its {Count(view.Widgets.Count, "widget")}");
        }
    }

    /// <summary>Added, removed and changed widgets between two versions of one view, or null when equal.</summary>
    private static string? CompareWidgets(ViewState old, ViewState @new)
    {
        var before = old.Widgets.ToDictionary(w => w.Id, StringComparer.Ordinal);
        var addedCount = 0;
        var changedCount = 0;

        foreach (var widget in @new.Widgets)
        {
            if (!before.Remove(widget.Id, out var previous))
            {
                addedCount++;
                continue;
            }

            if (!string.Equals(Fingerprint(previous), Fingerprint(widget), StringComparison.Ordinal))
            {
                changedCount++;
            }
        }

        var removedCount = before.Count;

        var parts = new List<string>(3);
        if (addedCount > 0)
        {
            parts.Add($"{Count(addedCount, "widget")} added");
        }

        if (removedCount > 0)
        {
            parts.Add($"{Count(removedCount, "widget")} removed");
        }

        if (changedCount > 0)
        {
            parts.Add($"{Count(changedCount, "widget")} changed");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string Fingerprint(WidgetInstance widget)
        => JsonSerializer.Serialize(widget, AppStateJsonContext.Default.WidgetInstance);

    private static void CompareDesign(DesignState old, DesignState @new, List<string> changes)
    {
        void Line<T>(string label, T before, T after, Func<T, string> say)
        {
            if (!EqualityComparer<T>.Default.Equals(before, after))
            {
                changes.Add($"{label}: {say(before)} → {say(after)}");
            }
        }

        static string Colour(string hex) => string.IsNullOrEmpty(hex) ? "theme" : hex;
        static string Px(double value) => $"{value.ToString("0.#", CultureInfo.InvariantCulture)}px";
        static string Percent(double value) => $"{(value * 100).ToString("0", CultureInfo.InvariantCulture)}%";
        static string Font(string family) => family == "system" ? "system default" : family;

        Line("Theme", old.Theme, @new.Theme, t => t.ToString());
        Line("Accent colour", old.PrimaryColor, @new.PrimaryColor, Colour);
        Line("Widget background", old.WidgetBackgroundColor, @new.WidgetBackgroundColor, Colour);
        Line("Widget opacity", old.SurfaceOpacity, @new.SurfaceOpacity, Percent);
        Line("Corner radius", old.CornerRadiusPx, @new.CornerRadiusPx, Px);
        Line("Border", old.BorderStyle, @new.BorderStyle, b => b.ToString());
        Line("Border width", old.BorderWidthPx, @new.BorderWidthPx, Px);
        Line("Border colour", old.BorderColor, @new.BorderColor, Colour);
        Line("Border opacity", old.BorderOpacity, @new.BorderOpacity, Percent);
        Line("Font", old.FontFamily, @new.FontFamily, Font);
        Line("Text size", old.FontScale, @new.FontScale, Percent);
        Line("Widget scale", old.UiScale, @new.UiScale, Percent);
        Line("Intro animation", old.IntroAnimation, @new.IntroAnimation, a => a.ToString());
        Line("Animation length", old.IntroAnimationMs, @new.IntroAnimationMs,
            ms => $"{ms.ToString("0", CultureInfo.InvariantCulture)}ms");
        Line("Gap between widgets", old.WidgetGapPx, @new.WidgetGapPx, Px);
        Line("Snap to grid", old.GridSnap, @new.GridSnap, on => on ? "on" : "off");
        Line("Header background", old.HeaderBackgroundColor, @new.HeaderBackgroundColor, Colour);
        Line("Header opacity", old.HeaderOpacity, @new.HeaderOpacity, Percent);
        Line("View change animation", old.TransitionAnimation, @new.TransitionAnimation, a => a.ToString());
        Line("View change animation length", old.TransitionAnimationMs, @new.TransitionAnimationMs,
            ms => $"{ms.ToString("0", CultureInfo.InvariantCulture)}ms");
        Line("Unlock animation", old.UnlockAnimation, @new.UnlockAnimation, a => a.ToString());
        Line("Unlock animation length", old.UnlockAnimationMs, @new.UnlockAnimationMs,
            ms => $"{ms.ToString("0", CultureInfo.InvariantCulture)}ms");
        Line("Startup sound", old.StartupSound, @new.StartupSound, on => on ? "on" : "off");
    }

    private static void ComparePreferences(AppPreferences old, AppPreferences @new, List<string> changes)
    {
        void Toggle(string label, bool before, bool after)
        {
            if (before != after)
            {
                changes.Add($"{label}: {(before ? "on" : "off")} → {(after ? "on" : "off")}");
            }
        }

        if (old.Language != @new.Language)
        {
            changes.Add($"Language: {old.Language} → {@new.Language}");
        }

        Toggle("Hide widgets in full-screen apps", old.GameModeSuspend, @new.GameModeSuspend);
        Toggle("Desktop icons", old.DesktopIconsVisible, @new.DesktopIconsVisible);
        Toggle("Local network access", old.AllowPrivateNetwork, @new.AllowPrivateNetwork);
        Toggle("Developer mode", old.DevMode, @new.DevMode);
        Toggle("Verbose logging", old.VerboseLogging, @new.VerboseLogging);
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
}
