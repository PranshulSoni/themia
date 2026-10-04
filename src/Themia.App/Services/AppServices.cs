namespace Themia.App.Services;

/// <summary>
/// The few services a widget view needs but cannot be handed.
/// <para>
/// Widget views are built by the catalogue from a <c>WidgetKind</c> alone, so there is no place to
/// inject into. The ticker is passed through <c>IWidgetView.Attach</c> because every widget needs
/// it; these are here because only a few widgets do, and threading them through every signature for
/// that would be worse than one explicit lookup.
/// </para>
/// <para>
/// Every one is a single shared instance, so however many mail or calendar widgets are on the
/// desktop there is one sync, one cache and one rate-limit budget per account.
/// </para>
/// </summary>
internal static class AppServices
{
    /// <summary>
    /// Themia's own accounts and every authorized request made for them — the seam mail, calendar,
    /// contacts, tasks, OneDrive, GitHub and Claude usage are built on. Null before the host has
    /// started (and in the gallery). See <see cref="Accounts.AccountService"/>.
    /// </summary>
    public static Accounts.AccountService? Accounts { get; internal set; }

    /// <summary>
    /// [S-459] Where screen-time widgets read from: the host's one tracker, or the debug gallery's
    /// fixture. Null before the host has started.
    /// </summary>
    public static IScreenTimeSource? ScreenTime { get; internal set; }

    /// <summary>
    /// [S-546] Where Mail widgets read from: the host's one <see cref="Mail.MailService"/>, or the
    /// debug gallery's fixture. Null before the host has started.
    /// </summary>
    public static Mail.IMailSource? Mail { get; internal set; }

    /// <summary>
    /// Open Settings on the Accounts page, for a widget whose empty state is "sign in". Null where
    /// there is no settings window to open (the gallery).
    /// </summary>
    public static Action? ShowAccounts { get; internal set; }

    /// <summary>
    /// [S-591] Where Tasks widgets read and write: account task lists and the lists kept on this PC.
    /// The host's one <see cref="Tasks.TaskService"/>, or the debug gallery's fixture.
    /// </summary>
    public static Tasks.TaskService? Tasks { get; internal set; }

    /// <summary>[S-607] Where OneDrive widgets list files: the host's one service, or the gallery's fixture.</summary>
    public static Files.DriveService? Drive { get; internal set; }

    /// <summary>
    /// [S-624] Where the GitHub widgets read from: the host's one <see cref="Developer.GitHubService"/>,
    /// or the debug gallery's fixture. Null before the host has started.
    /// </summary>
    public static Developer.IGitHubSource? GitHub { get; internal set; }

    /// <summary>[S-630] Where Claude usage widgets read from: the host's one service, or the gallery's fixture.</summary>
    public static Developer.IClaudeUsageSource? ClaudeUsage { get; internal set; }
}
