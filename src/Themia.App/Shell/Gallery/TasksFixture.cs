#if DEBUG
using Themia.App.Services;
using Themia.App.Services.Files;
using Themia.App.Services.Tasks;
using Themia.Core.Accounts;
using Themia.Core.Model;
using Themia.Core.Tasks;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// Made-up task lists and drive files for the debug gallery, so the Tasks and OneDrive widgets can
/// be drawn populated without signing in to anything.
/// <para>
/// The gallery runs instead of the app and has no accounts by design. Each widget gets two sources:
/// an empty one for its plain page — the no-account state a fresh install starts in — and a
/// populated one for its <c>+fixture</c> page. Everything is in memory: nothing is written and no
/// request leaves the machine. Debug builds only.
/// </para>
/// <para>
/// <c>THEMIA_TASKS_FIXTURE</c> picks another state for the Tasks fixture page while working on the
/// widget — <c>local</c>, <c>today</c>, <c>signin</c>, <c>offline</c>, <c>loading</c> or <c>empty</c> —
/// and <c>THEMIA_DRIVE_FIXTURE</c> the same for OneDrive: <c>signin</c>,
/// <c>offline</c>, <c>loading</c> or <c>empty</c>.
/// </para>
/// </summary>
internal static class TasksFixture
{
    private const string Account = "fixture-account";

    /// <summary>Put task sources where the Tasks widget looks for them.</summary>
    public static void Install(bool populated)
    {
        var state = populated ? State("THEMIA_TASKS_FIXTURE") : "none";

        LocalTaskList[] local = state == "local"
            ?
            [
                new("local-groceries", "Groceries",
                [
                    new("g1", "Oat milk", false), new("g2", "Coffee beans", false), new("g3", "Lemons", true),
                    new("g4", "Bread from the bakery on the corner, not the supermarket", false), new("g5", "Basil", false),
                ]),
            ]
            : [];

        AppServices.Tasks?.Dispose();
        AppServices.Tasks = new TaskService(new FixtureTaskRemote(state), new LocalTaskPool(store: null, local));
    }

    /// <summary>The config the populated page is drawn with, for the state the variable picked.</summary>
    public static TasksConfig Config() => State("THEMIA_TASKS_FIXTURE") switch
    {
        "local" => new TasksConfig { Source = TaskSource.Local, LocalListId = "local-groceries" },
        "today" => new TasksConfig { ListId = TaskRules.TodayListId, ShowCompleted = true },
        _ => new TasksConfig { ShowCompleted = true },
    };

    /// <summary>Put a drive where the OneDrive widget looks for one.</summary>
    public static void InstallDrive(bool populated)
    {
        AppServices.Drive?.Dispose();
        AppServices.Drive = new DriveService(new FixtureDriveRemote(populated ? State("THEMIA_DRIVE_FIXTURE") : "none"));
    }

    private static string State(string variable)
        => (Environment.GetEnvironmentVariable(variable) ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>A request that never answers, for the loading state.</summary>
    private static Task<T> Never<T>() => new TaskCompletionSource<T>().Task;

    private static AccountException Offline() => new(AccountError.Offline, "This PC is offline.");

    /// <summary>
    /// One Microsoft account with two lists. The due dates are relative to today, so every label —
    /// overdue, today, tomorrow, a weekday, a date — shows whatever day the gallery is drawn on.
    /// </summary>
    private sealed class FixtureTaskRemote(string state) : ITaskRemote
    {
        private readonly List<TaskItem> _tasks = state is "none" or "local" or "empty" ? [] : Seed();

        public IReadOnlyList<TaskAccount> Accounts => state switch
        {
            "none" or "local" => [],
            "signin" => [new TaskAccount(Account, AccountProvider.Microsoft, "Work", "Microsoft wants you to sign in again.")],
            _ => [new TaskAccount(Account, AccountProvider.Microsoft, "Work", null)],
        };

        public event Action? AccountsChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<TaskListInfo>> ListsAsync(string accountId, CancellationToken cancellation) => state switch
        {
            "offline" => Task.FromException<IReadOnlyList<TaskListInfo>>(Offline()),
            "loading" => Never<IReadOnlyList<TaskListInfo>>(),
            _ => Task.FromResult<IReadOnlyList<TaskListInfo>>([new("tasks", "Tasks", true), new("errands", "Errands", false)]),
        };

        public Task<IReadOnlyList<TaskItem>> TasksAsync(string accountId, string listId, bool showCompleted, CancellationToken cancellation)
            => Task.FromResult<IReadOnlyList<TaskItem>>([.. _tasks.Where(t => t.ListId == listId && (showCompleted || !t.Completed))]);

        public Task SetCompletedAsync(string accountId, string listId, string taskId, bool completed, CancellationToken cancellation)
        {
            Replace(taskId, t => t with { Completed = completed });
            return Task.CompletedTask;
        }

        public Task RenameAsync(string accountId, string listId, string taskId, string title, CancellationToken cancellation)
        {
            Replace(taskId, t => t with { Title = title });
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string accountId, string listId, string taskId, CancellationToken cancellation)
        {
            _tasks.RemoveAll(t => t.Id == taskId);
            return Task.CompletedTask;
        }

        public Task<TaskItem> CreateAsync(string accountId, string listId, string title, DateOnly? due, CancellationToken cancellation)
        {
            var task = new TaskItem($"fixture-{Guid.NewGuid():n}", listId, title, false, false, due is { } day ? TaskDue.Wire(day) : null);
            _tasks.Add(task);
            return Task.FromResult(task);
        }

        private void Replace(string id, Func<TaskItem, TaskItem> change)
        {
            var index = _tasks.FindIndex(t => t.Id == id);
            if (index >= 0)
            {
                _tasks[index] = change(_tasks[index]);
            }
        }

        private static List<TaskItem> Seed()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            string Day(int offset) => $"{TaskDue.Wire(today.AddDays(offset))}T00:00:00.0000000";

            return
            [
                new("t1", "tasks", "Send the quarterly report to Dana", false, true, Day(-2)),
                new("t2", "tasks", "Book the dentist", false, false, Day(0)),
                new("t3", "tasks", "Review the pull request for the new settings page before the release branch is cut", false, false, Day(1)),
                new("t4", "tasks", "Renew the car insurance", false, false, Day(4)),
                new("t5", "tasks", "Plan the team offsite", false, false, Day(16)),
                new("t6", "tasks", "Water the plants", false),
                new("t7", "tasks", "Back up the photo library", false),
                new("t8", "tasks", "Pay the electricity bill", true, false, Day(-1)),
                new("t9", "tasks", "Draft the agenda for Monday's planning", false, true, Day(2)),
                new("t10", "tasks", "Order new toner", false),
                new("t11", "tasks", "Call the landlord about the heating", false, false, Day(0)),
                new("t12", "tasks", "Update the onboarding checklist", false, false, Day(9)),
                new("t13", "tasks", "Return the library books", false),
                new("t14", "tasks", "Cancel the old phone contract", true),
                new("e2", "errands", "Collect the dry cleaning", false, false, Day(-1)),
                new("e1", "errands", "Pick up the parcel", false, false, Day(0)),
            ];
        }
    }

    /// <summary>A OneDrive with a fortnight of recent work in it, and a folder to walk into.</summary>
    private sealed class FixtureDriveRemote(string state) : IDriveRemote
    {
        public IReadOnlyList<DriveAccount> Accounts => state switch
        {
            "none" => [],
            "signin" => [new DriveAccount(Account, "Work", "Microsoft wants you to sign in again.")],
            _ => [new DriveAccount(Account, "Work", null)],
        };

        public event Action? AccountsChanged
        {
            add { }
            remove { }
        }

        public Task<DriveQuota?> QuotaAsync(string accountId, CancellationToken cancellation)
            => Task.FromResult<DriveQuota?>(state is "offline" or "loading" ? null : new DriveQuota(13_314_398_618, 1_099_511_627_776));

        public Task<IReadOnlyList<DriveItem>> ListAsync(string accountId, Uri address, int max, CancellationToken cancellation)
        {
            switch (state)
            {
                case "offline":
                    return Task.FromException<IReadOnlyList<DriveItem>>(Offline());
                case "loading":
                    return Never<IReadOnlyList<DriveItem>>();
                case "empty":
                    return Task.FromResult<IReadOnlyList<DriveItem>>([]);
            }

            var now = DateTimeOffset.Now;
            const string Web = "https://onedrive.live.com/";

            IReadOnlyList<DriveItem> items = address.AbsolutePath.Contains("/items/", StringComparison.Ordinal)
                ?
                [
                    new("p1", "Budget 2027.xlsx", 48_213, now.AddDays(-1), Web, false, null, "Dana Ruiz", "Projects"),
                    new("p2", "Kickoff.pptx", 2_604_118, now.AddDays(-3), Web, false, null, "Ann Lee", "Projects"),
                    new("p3", "Site photos", 0, now.AddDays(-9), Web, true, null, "Marcus Webb", "Projects", 48),
                ]
                :
                [
                    new("f1", "Projects", 0, now.AddHours(-2), Web, true, null, "Ann Lee", "My files", 3),
                    new("d1", "Quarterly report.docx", 184_320, now.AddMinutes(-12), Web, false, null, "Dana Ruiz", "Work › Reports"),
                    new("d2", "Team photo.jpg", 3_481_002, now.AddHours(-5), Web, false, "image/jpeg", "Ann Lee", "Pictures"),
                    new("d3", "Expenses.xlsx", 22_917, now.AddDays(-1), Web, false, null, "Ann Lee", "Work › Finance"),
                    new("d4", "Launch deck.pptx", 7_201_558, now.AddDays(-2), Web, false, null, "Marcus Webb", "Work › Launch"),
                    new("d5", "Interview notes.md", 3_112, now.AddDays(-3), Web, false, null, "Ann Lee", "Documents"),
                    new("d6", "Demo recording.mp4", 182_449_311, now.AddDays(-6), Web, false, "video/mp4", "Tom Okafor", "Videos"),
                    new("d7", "Contract signed.pdf", 412_600, now.AddDays(-12), Web, false, null, "Lena Fischer", "Work › Legal"),
                    new("d8", "Podcast intro.mp3", 5_660_120, now.AddDays(-20), Web, false, "audio/mpeg", "Ann Lee", "Music"),
                    new("d9", "Roadmap 2027.docx", 96_512, now.AddDays(-23), Web, false, null, "Dana Ruiz", "Work › Planning"),
                    new("d10", "Floor plan.png", 1_204_880, now.AddDays(-27), Web, false, "image/png", "Facilities", "Work › Office"),
                    new("d11", "Invoices", 0, now.AddDays(-30), Web, true, null, "Ann Lee", "Work › Finance", 112),
                    new("d12", "Release notes.md", 8_440, now.AddDays(-34), Web, false, null, "Tom Okafor", "Work › Launch"),
                    new("d13", "Survey results.xlsx", 310_211, now.AddDays(-41), Web, false, null, "Priya Nair", "Documents"),
                    new("d14", "Holiday.jpg", 2_880_114, now.AddDays(-45), Web, false, "image/jpeg", "Ann Lee", "Pictures › 2026"),
                    new("d15", "Brand guide.pdf", 5_402_331, now.AddDays(-52), Web, false, null, "Marcus Webb", "Work › Brand"),
                    new("d16", "Budget draft.xlsx", 41_207, now.AddDays(-60), Web, false, null, "Dana Ruiz", "Work › Finance"),
                    new("d17", "Workshop.pptx", 3_100_902, now.AddDays(-66), Web, false, null, "Lena Fischer", "Work › Training"),
                ];

            return Task.FromResult<IReadOnlyList<DriveItem>>([.. items.Take(max)]);
        }
    }
}
#endif
