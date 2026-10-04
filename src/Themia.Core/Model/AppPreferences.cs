using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The language the app's own text is shown in. The nine the original shipped, plus following
/// Windows. A closed set: each value picks a string table, not a parameter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppLanguage>))]
public enum AppLanguage
{
    /// <summary>The Windows display language, or English when it is not one of these.</summary>
    System = 0,
    English = 1,
    German = 2,
    Spanish = 3,
    French = 4,
    Italian = 5,
    Japanese = 6,
    Polish = 7,
    Portuguese = 8,
    Chinese = 9,
}

/// <summary>
/// [S-702] How the app behaves, as opposed to how widgets look (<see cref="DesignState"/>).
/// <para>
/// Kept apart from the design so an appearance preset or a design reset can never switch off, say,
/// the network guard or developer mode by accident.
/// </para>
/// </summary>
public sealed class AppPreferences
{
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Hide the widgets while a full-screen game or presentation has the screen.</summary>
    public bool GameModeSuspend { get; set; }

    /// <summary>Whether Explorer's desktop icons are shown under the widgets.</summary>
    public bool DesktopIconsVisible { get; set; } = true;

    /// <summary>
    /// Let widgets reach addresses on the local network and loopback. Off by default: a feed or
    /// web widget pointed at a router's admin page is the classic request-forgery hole.
    /// </summary>
    public bool AllowPrivateNetwork { get; set; }

    /// <summary>Developer tools in a release build (the widget gallery, diagnostics).</summary>
    public bool DevMode { get; set; }

    /// <summary>Write debug-level records to the log file, not just warnings and errors.</summary>
    public bool VerboseLogging { get; set; }

    public AppPreferences Clone() => new()
    {
        Language = Language,
        GameModeSuspend = GameModeSuspend,
        DesktopIconsVisible = DesktopIconsVisible,
        AllowPrivateNetwork = AllowPrivateNetwork,
        DevMode = DevMode,
        VerboseLogging = VerboseLogging,
    };
}
