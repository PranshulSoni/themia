#if DEBUG
using Themia.App.Services;
using Themia.App.Services.Mail;
using Themia.Core.Accounts;
using Themia.Core.Mail;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-557] A made-up inbox for the debug gallery, which has no accounts (and must never sign in to
/// one). Two pages: the plain one shows a fresh install's "no mail account" state, the
/// <c>+fixture</c> one a populated list across two accounts. Every sender, address and subject is
/// invented; the domains are the reserved example ones. Debug builds only.
/// <para>
/// <c>THEMIA_MAIL_FIXTURE</c> picks another state for the populated page while working on the
/// widget: <c>signin</c>, <c>offline</c>, <c>loading</c>, <c>empty</c> or <c>partial</c>; <c>compact</c>
/// and <c>row</c> draw the populated list in those views.
/// </para>
/// </summary>
internal sealed class MailFixture : IMailSource
{
    private static readonly MailAccount Work = new("fixture-work", AccountProvider.Microsoft, "Work", "ann.lee@contoso.com", null);
    private static readonly MailAccount Home = new("fixture-home", AccountProvider.Imap, "Home", "ann@example.org", null);

    private static readonly (string Account, int MinutesAgo, bool Read, string Name, string Address, string Subject, string Preview)[] Inbox =
    [
        ("fixture-work", 3, false, "Dana Ruiz", "dana.ruiz@contoso.com", "Q3 numbers for Thursday", "Hi Ann, attached are the revised figures. The EMEA line moved after the late invoices came in, so the totals on slide 4 changed."),
        ("fixture-home", 18, false, "Northwind Pharmacy", "no-reply@northwind.example", "Your prescription is ready", "It can be collected at the counter until Saturday. Bring this message or your card."),
        ("fixture-work", 64, false, "Build pipeline", "builds@fabrikam.com", "main: all checks passed", "Run 4812 finished in 11m 32s. 1,204 tests passed, 0 failed, 3 skipped."),
        ("fixture-work", 130, true, "Marcus Webb", "m.webb@contoso.com", "Re: Offsite agenda", "Works for me. Can we move the roadmap session after lunch so the design team can join?"),
        ("fixture-home", 290, true, "Priya Nair", "priya@example.net", "Photos from Sunday", "Finally sorted them! The ones by the lake came out really well, I put them in the shared album."),
        ("fixture-work", 1500, true, "Facilities", "facilities@contoso.com", "Building B closed on Friday", "Planned maintenance on the heating system. Please work from home or Building A."),
        ("fixture-home", 2900, false, "Tailspin Airlines", "bookings@tailspin.example", "Check-in is open for TS 418", "Your flight to Lisbon departs Tuesday at 07:40. Check in now to choose your seat."),
        ("fixture-work", 4400, true, "Lena Fischer", "lena.fischer@fabrikam.com", "Contract draft v3", "I've accepted most of your changes; two comments left in section 7 about the renewal term."),
        ("fixture-home", 8600, true, "City Library", "notices@library.example", "A book you reserved has arrived", "\"The Overstory\" is waiting for you at the main branch for the next 7 days."),
        ("fixture-work", 13000, true, "Tom Okafor", "tom.okafor@contoso.com", "Thanks for the review", "Merged it this morning. The retry change already cut the flaky failures in half."),
        ("fixture-home", 17500, true, "Electric Co.", "billing@power.example", "Your October statement", "Your statement is ready. Amount due: 64.20, payable by the 28th."),
        ("fixture-work", 21000, true, "Calendar", "calendar@contoso.com", "Updated invitation: Design sync", "The meeting moved to Wednesday 15:00 and now includes the accessibility review."),
        ("fixture-home", 26000, true, "Running Club", "hello@club.example", "Saturday route change", "We'll start from the boathouse this week because of the market in the square."),
        ("fixture-work", 30500, true, "Dana Ruiz", "dana.ruiz@contoso.com", "Re: Hiring plan", "Agreed on two roles for Q1. I'll draft the descriptions and share them by Friday."),
        ("fixture-home", 36000, true, "Bookshop", "orders@books.example", "Your order has shipped", "Two items are on their way and should arrive within three working days."),
        ("fixture-work", 41000, true, "IT Service Desk", "it@contoso.com", "Laptop refresh: pick a slot", "Your replacement laptop is ready. Choose a 30-minute slot to swap it over."),
        ("fixture-home", 47000, true, "Priya Nair", "priya@example.net", "Dinner on the 14th?", "We're thinking of the new place by the river. Does 19:30 work for you two?"),
        ("fixture-work", 52000, true, "Marcus Webb", "m.webb@contoso.com", "Design review notes", "Summary of Tuesday's review is in the wiki; three follow-ups are assigned to us."),
        ("fixture-home", 58000, true, "Insurance Co.", "service@insure.example", "Your policy renews next month", "Nothing changes in your cover. The new premium is shown in your account."),
        ("fixture-work", 64000, true, "Lena Fischer", "lena.fischer@fabrikam.com", "Signed copy attached", "Thanks for the quick turnaround. The countersigned contract is attached for your files."),
    ];

    private readonly bool _populated;
    private readonly string _state;

    private MailFixture(bool populated)
    {
        _populated = populated;
        _state = populated ? (Environment.GetEnvironmentVariable("THEMIA_MAIL_FIXTURE") ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
    }

    /// <summary>Put a fixture where Mail widgets look for their source.</summary>
    public static void Install(bool populated) => AppServices.Mail = new MailFixture(populated);

    /// <summary>The fixture page's config: the default, or the view <c>THEMIA_MAIL_FIXTURE</c> names.</summary>
    public static Themia.Core.Model.EmailConfig Config() => new()
    {
        ViewMode = Environment.GetEnvironmentVariable("THEMIA_MAIL_FIXTURE")?.Trim().ToLowerInvariant() switch
        {
            "compact" => MailViewMode.Compact,
            "row" => MailViewMode.Row,
            _ => MailViewMode.List,
        },
    };

    public event Action? AccountsChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<MailAccount> Accounts => !_populated
        ? []
        : _state == "signin"
            ? [Work with { Problem = "Microsoft wants you to sign in again." }]
            : [Work, Home];

    public IMailFeed Open(MailQuery query) => new Feed(Build(query));

    private MailFeedSnapshot Build(MailQuery query)
    {
        if (!_populated)
        {
            return MailFeedSnapshot.Empty;
        }

        // [S-1285] Folder counts as a probe reports them, for the big tile's summary line.
        MailAccountOutcome Ok(MailAccount a) => new(a.Id, MailAccountState.Ok, FetchStatus.Ok, null,
            a == Work ? new MailFolderStatus(1_284, 2) : new MailFolderStatus(377, 2));

        switch (_state)
        {
            case "loading":
                return new MailFeedSnapshot([], [], true);
            case "offline":
                return new MailFeedSnapshot([], [new(Work.Id, MailAccountState.Failed, FetchStatus.Offline, null), new(Home.Id, MailAccountState.Failed, FetchStatus.Offline, null)], false);
            case "empty":
                return new MailFeedSnapshot([], [Ok(Work), Ok(Home)], false);
            case "signin":
                return MailFeedSnapshot.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        var messages = Inbox
            .Where(m => query.AccountIds.Count == 0 || query.AccountIds.Contains(m.Account))
            .Where(m => _state != "partial" || m.Account == Work.Id)
            .Select(m => new MailMessage(
                m.Account,
                m.Account == Work.Id ? AccountProvider.Microsoft : AccountProvider.Imap,
                query.Folder,
                m.Account + "-" + m.MinutesAgo,
                m.Subject,
                m.Name,
                m.Address,
                now.AddMinutes(-m.MinutesAgo),
                m.Read,
                m.Preview,
                string.Empty))
            .Take(query.Count)
            .ToList();

        var outcomes = _state == "partial"
            ? new[] { Ok(Work), new MailAccountOutcome(Home.Id, MailAccountState.Failed, FetchStatus.Unreachable, null) }
            : [Ok(Work), Ok(Home)];

        return new MailFeedSnapshot(messages, outcomes, false);
    }

    public Task<string?> ChangeAsync(MailMessage message, MailChange change)
        => Task.FromResult<string?>("The gallery's inbox is made up; nothing was changed.");

    public bool Open(MailMessage message) => false;

    public bool Reply(MailMessage message) => false;

    private sealed class Feed(MailFeedSnapshot snapshot) : IMailFeed
    {
        public event Action? Updated
        {
            add { }
            remove { }
        }

        public MailFeedSnapshot Snapshot { get; } = snapshot;

        public void Refresh()
        {
            // The fixture never changes; there is nothing to fetch.
        }

        public void Dispose()
        {
            // Nothing held.
        }
    }
}
#endif
