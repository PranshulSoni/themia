using System.Globalization;
using Themia.Core.Widgets;

namespace Themia.Core.Mail;

/// <summary>Where one account stands in a feed.</summary>
public enum MailAccountState
{
    /// <summary>Asked, no answer yet.</summary>
    Pending,

    /// <summary>The last fetch worked.</summary>
    Ok,

    /// <summary>The last attempt failed; <see cref="MailAccountOutcome.Status"/> says how.</summary>
    Failed,
}

/// <summary>How one account's part of a feed last went.</summary>
public sealed record MailAccountOutcome(string AccountId, MailAccountState State, FetchStatus Status, string? Detail);

/// <summary>
/// [S-546] What a widget asks the mail service for. Two widgets asking the same thing share one feed
/// and one set of requests, so the key is order-insensitive in its accounts.
/// </summary>
public sealed record MailQuery(IReadOnlyList<string> AccountIds, MailFolder Folder, int Count)
{
    public string Key => string.Join(",", AccountIds.Order(StringComparer.Ordinal)) + "|" + Folder.Wire() + "|" + Count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A feed's state at one moment: the merged list (newest first) and how each account last went.</summary>
public sealed record MailFeedSnapshot(IReadOnlyList<MailMessage> Messages, IReadOnlyList<MailAccountOutcome> Outcomes, bool Busy)
{
    public static MailFeedSnapshot Empty { get; } = new([], [], false);
}

/// <summary>[S-548] Merging per-account lists into the one list a widget shows.</summary>
public static class MailList
{
    /// <summary>
    /// The newest <paramref name="count"/> messages across every account, then in the order asked.
    /// "Oldest first" is the same newest N turned round — not the N oldest messages in the mailbox,
    /// which nobody asking for a mail widget means. A message with no date sorts after every dated
    /// one; ties keep the accounts' order.
    /// </summary>
    public static IReadOnlyList<MailMessage> Merge(IEnumerable<IReadOnlyList<MailMessage>> perAccount, int count, MailSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(perAccount);

        var newest = perAccount
            .SelectMany(list => list)
            .Select((message, index) => (message, index))
            .OrderByDescending(x => x.message.Received.HasValue)
            .ThenByDescending(x => x.message.Received ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.index)
            .Take(Math.Max(0, count))
            .Select(x => x.message)
            .ToList();

        if (order == MailSortOrder.OldestFirst)
        {
            newest.Reverse();
        }

        return newest;
    }

    /// <summary>The header badge: unread messages among those shown.</summary>
    public static int Unread(IReadOnlyList<MailMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages.Count(m => !m.IsRead);
    }
}

/// <summary>
/// [S-546] When to ask again. Each account's folder counts are probed on a cadence — one cheap
/// request — and the list is fetched only when they moved, or when it is old enough that a change
/// the counts cannot see (a message read and another arriving between two probes) might be hiding.
/// </summary>
public static class MailRefresh
{
    /// <summary>How often the counts are probed. Mail a minute or two late is fine; a request a second is not.</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromMinutes(2);

    /// <summary>The list is fetched again at least this often, whatever the counts say.</summary>
    public static readonly TimeSpan FullRefreshAfter = TimeSpan.FromMinutes(15);

    public static bool NeedsFetch(MailFolderStatus? known, MailFolderStatus? fresh, DateTimeOffset? lastFetch, DateTimeOffset now)
    {
        if (lastFetch is not { } fetched || known is null || fresh is null)
        {
            return true;
        }

        return known != fresh || now - fetched >= FullRefreshAfter || now < fetched;
    }
}

/// <summary>What the widget's body shows.</summary>
public enum MailScreenKind
{
    /// <summary>No mail-capable account is signed in to Themia at all.</summary>
    NoAccounts,

    /// <summary>The widget names accounts and none of them is signed in any more.</summary>
    AccountsGone,

    /// <summary>Every account it would read needs the user to sign in again.</summary>
    SignInRequired,

    /// <summary>Asked, nothing back yet, nothing to show meanwhile.</summary>
    Loading,

    /// <summary>Every account failed and there is nothing to show.</summary>
    Failed,

    /// <summary>Fetched, and the folder is empty.</summary>
    Empty,

    /// <summary>A list. <see cref="MailScreen.Warning"/> may still name an account that failed.</summary>
    Messages,
}

/// <param name="OffersAccounts">Whether the screen carries a way to the Accounts settings page.</param>
/// <param name="Warning">For a list: one account that could not be read, in a sentence; else null.</param>
public sealed record MailScreen(MailScreenKind Kind, string Text, bool OffersAccounts, string? Warning);

/// <summary>[S-549] Which accounts a widget reads, and which screen it shows.</summary>
public static class MailScreens
{
    /// <summary>
    /// The accounts a widget reads: every mail account when it names none, else the ones it names
    /// that are still signed in, in the accounts' own order.
    /// </summary>
    public static IReadOnlyList<MailAccount> Targets(IReadOnlyList<MailAccount> mailAccounts, IReadOnlyList<string> chosen)
    {
        ArgumentNullException.ThrowIfNull(mailAccounts);
        ArgumentNullException.ThrowIfNull(chosen);

        if (chosen.Count == 0)
        {
            return mailAccounts;
        }

        var wanted = new HashSet<string>(chosen, StringComparer.Ordinal);
        return [.. mailAccounts.Where(a => wanted.Contains(a.Id))];
    }

    public static MailScreen Decide(
        IReadOnlyList<MailAccount> mailAccounts,
        IReadOnlyList<string> chosen,
        IReadOnlyList<MailAccountOutcome> outcomes,
        int messageCount,
        MailFolder folder)
    {
        ArgumentNullException.ThrowIfNull(mailAccounts);
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(outcomes);

        if (mailAccounts.Count == 0)
        {
            return new MailScreen(MailScreenKind.NoAccounts, "No mail account yet.", true, null);
        }

        var targets = Targets(mailAccounts, chosen);
        if (targets.Count == 0)
        {
            return new MailScreen(MailScreenKind.AccountsGone, "The mail account this widget showed is no longer signed in.", true, null);
        }

        var usable = targets.Where(a => a.Problem is null).ToList();
        var broken = targets.Where(a => a.Problem is not null).ToList();

        if (usable.Count == 0)
        {
            var text = broken.Count == 1
                ? $"{broken[0].Label} needs you to sign in again."
                : "Your mail accounts need you to sign in again.";
            return new MailScreen(MailScreenKind.SignInRequired, text, true, null);
        }

        var byId = outcomes.GroupBy(o => o.AccountId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var states = usable
            .Select(a => (Account: a, Outcome: byId.TryGetValue(a.Id, out var o) ? o : null))
            .ToList();
        var failed = states.Where(s => s.Outcome is { State: MailAccountState.Failed }).ToList();

        var warning = broken.Count > 0
            ? $"{broken[0].Label} needs you to sign in again."
            : failed.Count > 0
                ? $"Could not refresh {failed[0].Account.Label}: {Lower(Describe(failed[0].Outcome!.Status))}."
                : null;

        if (messageCount > 0)
        {
            return new MailScreen(MailScreenKind.Messages, string.Empty, broken.Count > 0, warning);
        }

        if (states.Any(s => s.Outcome is null or { State: MailAccountState.Pending }))
        {
            return new MailScreen(MailScreenKind.Loading, "Loading mail…", false, null);
        }

        if (failed.Count == states.Count)
        {
            var status = failed[0].Outcome!.Status;
            var signIn = status == FetchStatus.NotConfigured;
            var text = signIn
                ? $"{failed[0].Account.Label} needs you to sign in again."
                : status == FetchStatus.Offline
                    ? "No network. Mail refreshes when you're back online."
                    : Describe(status) + ".";
            return new MailScreen(signIn ? MailScreenKind.SignInRequired : MailScreenKind.Failed, text, signIn, null);
        }

        return new MailScreen(MailScreenKind.Empty, $"No messages in {folder.Label()}.", false, warning);
    }

    /// <summary>A failure in mail's own words; the generic sentence where mail has nothing better to say.</summary>
    public static string Describe(FetchStatus status) => status switch
    {
        FetchStatus.Offline => "No network",
        FetchStatus.Unreachable => "Can't reach the mail server",
        FetchStatus.TimedOut => "The mail server did not answer in time",
        FetchStatus.RateLimited => "The mail service asked Themia to slow down",
        FetchStatus.NotConfigured => "The account needs you to sign in again",
        FetchStatus.Refused => "The mail server refused the request",
        _ => "The mail service sent something unexpected",
    };

    private static string Lower(string sentence)
        => sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;
}

/// <summary>[S-548] When a message arrived, in a list's few characters.</summary>
public static class MailTime
{
    /// <summary>List and compact rows: now, 5m, 3h, 2d, then a date.</summary>
    public static string Short(DateTimeOffset? when, DateTimeOffset now, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (when is not { } at)
        {
            return string.Empty;
        }

        var age = now - at;

        if (age < TimeSpan.FromMinutes(1))
        {
            return "now"; // includes a provider clock running ahead of this one
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours}h";
        }

        if (age < TimeSpan.FromDays(7))
        {
            return $"{(int)age.TotalDays}d";
        }

        return MonthDay(at.ToOffset(now.Offset), culture);
    }

    /// <summary>The row view's date column: a time today, a weekday this week, then a date.</summary>
    public static string Full(DateTimeOffset? when, DateTimeOffset now, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (when is not { } at)
        {
            return string.Empty;
        }

        var local = at.ToOffset(now.Offset);
        var age = now - at;

        if (age < TimeSpan.FromDays(1))
        {
            return local.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        }

        return age < TimeSpan.FromDays(7) ? local.ToString("ddd", culture) : MonthDay(local, culture);
    }

    /// <summary>
    /// "Sep 28". A fixed pattern rather than the culture's month-day pattern: the app runs with
    /// invariant globalization, whose pattern is the long "September 28".
    /// </summary>
    private static string MonthDay(DateTimeOffset at, CultureInfo culture) => at.ToString("MMM d", culture);
}
