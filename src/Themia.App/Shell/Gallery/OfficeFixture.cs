#if DEBUG
using Themia.App.Services.Accounts;
using Themia.App.Services.Office;
using Themia.Core.Accounts;
using Themia.Core.Model;
using Themia.Core.Office;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-589] A made-up work and personal account for the debug gallery's calendar and contacts pages.
/// <para>
/// The gallery runs instead of the app, so there are no accounts (<c>AppServices.Accounts</c> is
/// null) and the plain pages show the no-account state, which is one of the states worth seeing.
/// The <c>+fixture</c> pages install this instead: two accounts, a week of meetings around today
/// (overlapping ones for the day lanes, a two-day all-day event, an evening class), three
/// birthdays and two dozen people. All fictional, all in memory, all relative to today so the
/// pages never go stale. Debug builds only.
/// </para>
/// <para>
/// <c>THEMIA_GALLERY_LAYOUT</c> (<c>Week</c>, <c>Day</c>, <c>Agenda</c>, <c>Compact</c>,
/// <c>Details</c>, <c>Grid</c>…) picks the view the fixture config starts in, so every layout can
/// be rendered without editing code.
/// </para>
/// </summary>
internal sealed class OfficeFixture : IOfficeSource
{
    private const string Work = "microsoft:fixture-work";
    private const string Home = "google:fixture-home";

    private static readonly IReadOnlyList<AccountInfo> AllAccounts =
    [
        new(Work, AccountProvider.Microsoft, "alex.morgan@contoso.com", "Alex Morgan", "Work", null, null, null),
        new(Home, AccountProvider.Google, "alex.morgan@example.com", "Alex Morgan", "Personal", null, null, null),
    ];

    private static readonly (string Name, string? Email, string? Phone, string? Title, string? Company)[] People =
    [
        ("Amara Okafor", "amara.okafor@contoso.com", "+41 44 555 01 12", "Product Manager", "Contoso"),
        ("Ben Hartley", "ben@fabrikam.example", "+44 20 7946 0321", "Account Director", "Fabrikam"),
        ("Carla Ruiz", "carla.ruiz@contoso.com", null, "UX Researcher", "Contoso"),
        ("Daniel Weber", null, "+49 30 555 0142", "Electrician", null),
        ("Elena Petrova", "elena.p@example.com", "+41 79 555 23 90", null, null),
        ("Felix Brandt", "felix.brandt@contoso.com", "+41 44 555 01 40", "Engineering Lead", "Contoso"),
        ("Grace Kim", "grace.kim@northwind.example", null, "CFO", "Northwind Traders"),
        ("Hannah Schmid", "hannah.schmid@example.com", "+41 78 555 61 04", null, null),
        ("Ivan Horvat", "ivan@adatum.example", "+385 1 555 0199", "Consultant", "Adatum"),
        ("Jonas Meier", "jonas.meier@contoso.com", "+41 44 555 01 77", "Designer", "Contoso"),
        ("Keiko Tanaka", "keiko.tanaka@example.com", null, "Photographer", null),
        ("Lena Fischer", "lena.fischer@example.com", "+41 76 555 38 21", null, null),
        ("Marco Bianchi", "marco@tailspin.example", "+39 02 5550 1987", "Pilot", "Tailspin Toys"),
        ("Mia Chen", "mia.chen@contoso.com", "+41 44 555 01 03", "Data Scientist", "Contoso"),
        ("Noah Fischer", null, "+41 79 555 90 12", null, null),
        ("Olivia Brown", "olivia.brown@fabrikam.example", null, "Legal Counsel", "Fabrikam"),
        ("Priya Shah", "priya.shah@contoso.com", "+41 44 555 01 58", "Head of Design", "Contoso"),
        ("Rafael Costa", "rafael.costa@example.com", "+351 21 555 0137", null, null),
        ("Sam Taylor", "sam.taylor@example.com", "+41 77 555 44 10", "Teacher", "City School"),
        ("Sofia Rossi", "sofia@wingtip.example", null, "Architect", "Wingtip"),
        ("Tom Becker", "tom.becker@example.com", "+49 89 555 0174", null, null),
        ("Uma Patel", "uma.patel@contoso.com", "+41 44 555 01 91", "Recruiter", "Contoso"),
        ("Victor Lindqvist", "victor@litware.example", "+46 8 555 012 34", "CTO", "Litware"),
        ("Yara Haddad", "yara.haddad@example.com", null, "Doctor", "City Hospital"),

        // [S-1279] Enough people for an XL tile's three columns to scroll.
        ("Aiko Watanabe", "aiko.watanabe@contoso.com", "+41 44 555 02 11", "Translator", "Contoso"),
        ("Bruno Keller", "bruno.keller@example.com", "+41 79 555 12 34", "Plumber", null),
        ("Chloé Martin", "chloe.martin@fabrikam.example", "+33 1 55 50 12 34", "Sales Lead", "Fabrikam"),
        ("David Okoro", "david.okoro@contoso.com", "+41 44 555 02 27", "QA Engineer", "Contoso"),
        ("Emma Lindgren", "emma@litware.example", null, "Product Owner", "Litware"),
        ("Farah Aziz", "farah.aziz@example.com", "+44 7700 900123", null, null),
        ("Gabriel Silva", "gabriel.silva@contoso.com", "+41 44 555 02 39", "Support Engineer", "Contoso"),
        ("Hugo Laurent", null, "+33 6 55 50 98 76", "Chef", "Le Petit Bistro"),
        ("Isabel García", "isabel.garcia@adatum.example", "+34 91 555 0123", "Analyst", "Adatum"),
        ("Jakob Novak", "jakob.novak@example.com", "+420 2 5550 1234", null, null),
        ("Kira Ivanova", "kira.ivanova@contoso.com", "+41 44 555 02 52", "Office Manager", "Contoso"),
        ("Leo Schwarz", "leo.schwarz@example.com", null, "Student", null),
        ("Maya Rosen", "maya.rosen@northwind.example", "+972 3 555 0199", "Controller", "Northwind Traders"),
        ("Nils Andersen", "nils.andersen@contoso.com", "+41 44 555 02 64", "DevOps Engineer", "Contoso"),
        ("Oskar Wolf", null, "+49 40 555 0161", "Landlord", null),
        ("Paula Moreno", "paula.moreno@example.com", "+41 76 555 71 02", "Yoga Teacher", "Studio Kreis 4"),
        ("Quentin Blake", "quentin@wingtip.example", "+44 20 7946 0888", "Illustrator", "Wingtip"),
        ("Rosa Ferrari", "rosa.ferrari@contoso.com", "+41 44 555 02 75", "Finance", "Contoso"),
    ];

    private readonly bool _populated;

    private OfficeFixture(bool populated) => _populated = populated;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Put a fixture where the widgets look: the populated one for a <c>+fixture</c> page, none at
    /// all for a plain page (no accounts service, exactly as the gallery is without one).
    /// </summary>
    public static void Install(bool populated) => OfficeSources.Fixture = populated ? new OfficeFixture(true) : null;

    /// <summary>The calendar config a <c>+fixture</c> page starts with.</summary>
    public static CalendarConfig CalendarConfig()
    {
        var config = new CalendarConfig();
        if (Enum.TryParse<CalendarViewMode>(Environment.GetEnvironmentVariable("THEMIA_GALLERY_LAYOUT"), true, out var view))
        {
            config.View = view;
        }

        return config;
    }

    public static ContactsConfig ContactsConfig()
    {
        var config = new ContactsConfig();
        if (Enum.TryParse<ContactsLayout>(Environment.GetEnvironmentVariable("THEMIA_GALLERY_LAYOUT"), true, out var layout))
        {
            config.Layout = layout;
        }

        return config;
    }

    public IReadOnlyList<AccountInfo> Accounts(AccountFeatures feature)
        => _populated ? [.. AllAccounts.Where(a => (a.Features & feature) != 0)] : [];

    public Task<Synced<IReadOnlyList<CalendarEvent>>> EventsAsync(CalendarTarget target, DateTimeOffset from, DateTimeOffset to, bool force, CancellationToken cancellation)
    {
        var events = Events(target.AccountId)
            .Where(e => e.AllDay
                ? CalendarRules.LocalMidnight(e.FirstDay, TimeZoneInfo.Local) < to && CalendarRules.LocalMidnight(e.EndDay, TimeZoneInfo.Local) > from
                : e.Start < to && e.End > from)
            .ToList();

        return Task.FromResult(Ok<IReadOnlyList<CalendarEvent>>(events));
    }

    public Task<Synced<IReadOnlyList<CalendarInfo>>> CalendarsAsync(string accountId, bool force, CancellationToken cancellation)
        => Task.FromResult(Ok<IReadOnlyList<CalendarInfo>>(accountId == Work
            ? [new("work-default", "Calendar", true, "#0078d4"), new("work-team", "Team holidays", false, null)]
            : [new("primary", "Alex Morgan", true, "#16a765"), new("family", "Family", false, "#f691b2")]));

    public Task<Synced<IReadOnlyList<Birthday>>> BirthdaysAsync(string accountId, bool force, CancellationToken cancellation)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        IReadOnlyList<Birthday> birthdays = accountId == Home
            ? [
                new("Mia Chen", today.Month, today.Day, 1994),
                new("Lena Fischer", today.AddDays(2).Month, today.AddDays(2).Day, 1990),
                new("Tom Becker", today.AddDays(11).Month, today.AddDays(11).Day, null),
            ]
            : [];

        return Task.FromResult(Ok(birthdays));
    }

    public Task<Synced<IReadOnlyList<Contact>>> ContactsAsync(string accountId, int max, bool force, CancellationToken cancellation)
    {
        // Work holds the company's people, Personal everyone else; the merge puts them in one list.
        var contacts = People
            .Where(p => (accountId == Work) == (p.Company == "Contoso"))
            .Select(p => new Contact
            {
                Id = $"{accountId}/{p.Name}",
                Name = p.Name,
                Email = p.Email,
                Phone = p.Phone,
                JobTitle = p.Title,
                Company = p.Company,
                AccountId = accountId,
            })
            .Take(max)
            .ToList();

        return Task.FromResult(Ok<IReadOnlyList<Contact>>(contacts));
    }

    private static Synced<T> Ok<T>(T value)
        where T : class
        => new(value, SyncProblem.None, null, DateTimeOffset.UtcNow);

    /// <summary>The account's events around today, built fresh so the day boundaries are today's.</summary>
    private static IEnumerable<CalendarEvent> Events(string accountId)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        CalendarEvent At(int day, int hour, int minute, int minutes, string subject, string? location = null, string? organizer = null)
        {
            var start = CalendarRules.LocalMidnight(today.AddDays(day), TimeZoneInfo.Local).AddHours(hour).AddMinutes(minute);
            return new CalendarEvent
            {
                Id = $"{accountId}/{day}/{hour}{minute}/{subject}",
                Subject = subject,
                Start = start,
                End = start.AddMinutes(minutes),
                Location = location,
                Organizer = organizer,
                WebLink = accountId == Work ? "https://outlook.office.com/calendar/view/day" : "https://calendar.google.com/calendar/r",
            };
        }

        CalendarEvent AllDay(int day, int days, string subject)
            => CalendarEvent.AllDayEvent($"{accountId}/{day}/all/{subject}", subject, today.AddDays(day), today.AddDays(day + days));

        if (accountId == Work)
        {
            return
            [
                At(0, 9, 0, 15, "Team standup", "Microsoft Teams"),
                At(0, 11, 30, 60, "Design review", "Room 4.12", "Priya Shah"),
                At(0, 14, 0, 30, "1:1 with Jonas", null, "Jonas Meier"),
                At(0, 15, 0, 90, "Quarterly planning", "Board room"),
                At(0, 16, 0, 60, "Customer call — Fabrikam", "Microsoft Teams", "Ben Hartley"),
                At(1, 9, 0, 15, "Team standup", "Microsoft Teams"),
                At(1, 13, 0, 60, "Lunch with Sam", "Café Odeon"),
                At(2, 10, 0, 60, "Sprint retro", "Room 2.03"),
                AllDay(3, 2, "Offsite — Lucerne"),
                At(6, 17, 0, 30, "Release 4.2 go/no-go", "Microsoft Teams"),
                At(8, 7, 10, 95, "Flight LX 974 to Berlin", "Zurich Airport"),
                At(-3, 10, 0, 60, "Board prep", "Board room", "Grace Kim"),
                At(-6, 15, 30, 45, "Interview: frontend engineer", "Room 1.10", "Uma Patel"),
                AllDay(12, 1, "Public holiday"),
                At(15, 9, 30, 120, "Workshop: accessibility", "Room 4.12"),
                At(20, 11, 0, 60, "Vendor review — Litware", null, "Victor Lindqvist"),
            ];
        }

        return
        [
            At(0, 18, 30, 90, "Yoga", "Studio Kreis 4"),
            At(1, 8, 0, 45, "Dentist", "Dr. Keller, Seefeldstrasse"),
            At(5, 19, 0, 150, "Dinner at Nonna's"),
            At(-1, 9, 0, 120, "Farmers market"),
            At(6, 9, 0, 120, "Farmers market"),
            At(13, 9, 0, 120, "Farmers market"),
            AllDay(-9, 3, "Weekend in Ticino"),
            At(17, 20, 0, 150, "Concert — Tonhalle", "Tonhalle Zürich"),
        ];
    }
}
#endif
