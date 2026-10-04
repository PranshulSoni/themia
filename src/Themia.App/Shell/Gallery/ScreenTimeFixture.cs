#if DEBUG
using Themia.App.Services;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-459] A made-up month of screen time for the debug gallery.
/// <para>
/// The gallery runs instead of the app, so no tracker exists there — by design: rendering a widget
/// must not hook the foreground or write a history file beside the user's state. Without a source
/// the widget could only ever show its empty state, so the gallery installs this one, which holds
/// a deterministic month in memory and never touches the disk. Debug builds only.
/// </para>
/// <para>
/// Two of them: an empty one for the plain gallery page, which shows the state a fresh install
/// starts in, and a populated one for the <c>+fixture</c> page.
/// </para>
/// </summary>
internal sealed class ScreenTimeFixture(bool populated) : IScreenTimeSource
{
    private static readonly (string App, string Name, string? Path, int Weight)[] Apps =
    [
        ("msedge", "Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", 34),
        ("code", "Visual Studio Code", null, 27),
        ("explorer", "Windows Explorer", @"C:\Windows\explorer.exe", 12),
        ("windowsterminal", "Windows Terminal", null, 9),
        ("notepad", "Notepad", @"C:\Windows\System32\notepad.exe", 6),
        ("taskmgr", "Task Manager", @"C:\Windows\System32\Taskmgr.exe", 4),
        ("cmd", "Command Prompt", @"C:\Windows\System32\cmd.exe", 3),
        ("mmc", "Microsoft Management Console", @"C:\Windows\System32\mmc.exe", 2),
        ("regedit", "Registry Editor", @"C:\Windows\regedit.exe", 2),
        ("charmap", "Character Map", @"C:\Windows\System32\charmap.exe", 1),
    ];

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public LocalDay Today => LocalDay.From(DateTimeOffset.Now);

    /// <summary>
    /// Put a fixture where the widgets look for their source — unless the app's real tracker is
    /// there. The gallery can also be opened from inside the running app, and a debug page must
    /// never take the user's own screen time out from under their widgets and settings.
    /// </summary>
    public static void Install(bool populated)
    {
        if (AppServices.ScreenTime is not null and not ScreenTimeFixture)
        {
            return;
        }

        AppServices.ScreenTime = new ScreenTimeFixture(populated);
    }

    public ScreenTimeDay Day(string? date)
    {
        var key = date ?? Today.Key;
        return Build(key, LocalDay.TryParse(key, out var day) ? Today.DaysUntil(day) : 0);
    }

    public IReadOnlyList<ScreenTimeDay> Range(int days)
    {
        var length = Math.Clamp(days, 1, ScreenTimeTracker.RetentionDays);
        var result = new List<ScreenTimeDay>(length);

        for (var back = length - 1; back >= 0; back--)
        {
            result.Add(Build(Today.AddDays(-back).Key, -back));
        }

        return result;
    }

    /// <summary>A plausible day: a weekday-shaped total, split by weight with a little per-day wobble.</summary>
    private ScreenTimeDay Build(string date, int offset)
    {
        if (!populated || offset > 0)
        {
            return new ScreenTimeDay(date, 0, [], 0);
        }

        var seed = Math.Abs(offset * 7919) % 97;
        var weekend = LocalDay.TryParse(date, out var day) && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        // Today is a day in progress; one day in nine was spent away from the desk.
        var hours = offset == 0 ? 4.6 : seed % 9 == 4 ? 0 : (weekend ? 2.2 : 6.5) + (seed % 30 / 10.0);
        var total = (long)(hours * 3600);

        var apps = new List<ScreenTimeApp>();
        var weights = Apps.Sum(a => a.Weight);
        long used = 0;

        for (var i = 0; i < Apps.Length && total > 0; i++)
        {
            var (app, name, path, weight) = Apps[i];
            var wobble = 1 + (((seed + (i * 13)) % 7) - 3) / 10.0;
            var seconds = (long)(total * weight / (double)weights * wobble);

            if (seconds <= 0)
            {
                continue;
            }

            apps.Add(new ScreenTimeApp(app, name, seconds, path));
            used += seconds;
        }

        apps.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        return new ScreenTimeDay(date, used, apps, total / 300);
    }
}
#endif
