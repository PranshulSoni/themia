using Themia.Core.Accounts;

namespace Themia.Core.Office;

/// <summary>
/// [S-588] Why the calendar or contacts widget has nothing (or nothing new) from an account. Named,
/// because each needs a different thing from the user: signing in again and granting access are
/// done on the Accounts page, the network ones fix themselves, and the rest go to the log.
/// </summary>
public enum SyncProblem
{
    None,

    /// <summary>The account the widget names has been signed out.</summary>
    NoAccount,

    /// <summary>The sign-in was revoked, the password changed, or the secret cannot be opened here.</summary>
    SignInRequired,

    /// <summary>Signed in, but this account did not grant calendar or contacts access (403).</summary>
    Forbidden,

    Offline,
    Unreachable,
    TimedOut,
    RateLimited,
    ServerError,
    BadResponse,
}

public static class SyncProblems
{
    public static SyncProblem From(AccountError error) => error switch
    {
        AccountError.UnknownAccount => SyncProblem.NoAccount,
        AccountError.SignInRequired => SyncProblem.SignInRequired,
        AccountError.Forbidden => SyncProblem.Forbidden,
        AccountError.Offline => SyncProblem.Offline,
        AccountError.Unreachable => SyncProblem.Unreachable,
        AccountError.TimedOut => SyncProblem.TimedOut,
        AccountError.RateLimited => SyncProblem.RateLimited,
        AccountError.ServerError => SyncProblem.ServerError,
        _ => SyncProblem.BadResponse,
    };

    /// <summary>Whether only the user can mend it, on the Accounts page.</summary>
    public static bool NeedsUser(SyncProblem problem)
        => problem is SyncProblem.NoAccount or SyncProblem.SignInRequired or SyncProblem.Forbidden;

    /// <summary>
    /// [S-588] How long a failure is held before the same request is tried again on a timer. A
    /// refusal only the user can mend is retried hourly, not every poll (the original's [S-39]); a
    /// rate limit waits a quarter of an hour; anything else is retried on the next tick.
    /// </summary>
    public static TimeSpan HoldFor(SyncProblem problem) => problem switch
    {
        SyncProblem.SignInRequired or SyncProblem.Forbidden or SyncProblem.NoAccount => TimeSpan.FromHours(1),
        SyncProblem.RateLimited => TimeSpan.FromMinutes(15),
        _ => TimeSpan.Zero,
    };

    /// <summary>One sentence for the widget, naming what is wrong for <paramref name="what"/> ("calendar", "contacts").</summary>
    public static string Message(SyncProblem problem, string what) => problem switch
    {
        SyncProblem.None => string.Empty,
        SyncProblem.NoAccount => "That account is no longer signed in.",
        SyncProblem.SignInRequired => "Sign in again to see this.",
        SyncProblem.Forbidden => $"Access to the {what} was not granted.",
        SyncProblem.Offline => "No network",
        SyncProblem.Unreachable => "Can't reach the service",
        SyncProblem.TimedOut => "The service timed out",
        SyncProblem.RateLimited => "Asked too often — waiting a while",
        SyncProblem.ServerError => "The service is having trouble",
        _ => "The service sent something unexpected",
    };
}
