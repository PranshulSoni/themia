using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>
/// [S-943] Curated search words for the widget picker, ported from the original's
/// <c>searchKeywords.ts</c>.
/// <para>
/// A widget's label and description are written for reading, not for searching: nobody reads
/// "Bytes per second across the active adapters" and types that. These are the words people reach
/// for instead: what the widget shows ("wifi", "temperature"), the vendor or protocol behind it
/// ("outlook", "imap"), the job it does ("timer", "launcher") and the other common spelling
/// ("todo" for tasks).
/// </para>
/// <para>
/// English only, on purpose: the localisation round indexes the translated label and description
/// next to the English ones, so this list keeps working in every language without being written
/// nine times.
/// </para>
/// </summary>
public static class WidgetKeywords
{
    private static readonly Dictionary<WidgetKind, string[]> Keywords = new()
    {
        [WidgetKind.Folder] = ["files", "explorer", "directory", "file browser", "documents", "downloads"],
        [WidgetKind.Email] = ["email", "inbox", "outlook", "gmail", "imap", "messages", "unread"],
        [WidgetKind.Contacts] = ["address book", "people", "phone numbers", "directory"],
        [WidgetKind.Calendar] = ["events", "meetings", "appointments", "agenda", "schedule", "outlook", "google calendar", "birthdays"],
        [WidgetKind.Tasks] = ["todo", "to-do", "checklist", "reminders", "microsoft to do", "google tasks"],
        [WidgetKind.OneDrive] = ["cloud files", "sharepoint", "microsoft", "recent files"],

        [WidgetKind.GitHub] = ["git", "repository", "repo", "pull requests", "ci", "actions", "commits", "code"],
        [WidgetKind.GitHubNotifications] = ["git", "repo", "inbox", "mentions", "alerts"],
        [WidgetKind.GitHubReviews] = ["git", "repo", "pull requests", "pr", "code review", "review requested"],
        [WidgetKind.GitHubIssues] = ["git", "repo", "tickets", "bugs", "backlog"],
        [WidgetKind.ClaudeUsage] = ["ai", "anthropic", "tokens", "quota", "limits", "claude code"],

        [WidgetKind.WeatherNow] = ["temperature", "forecast", "rain", "wind", "humidity", "climate", "degrees"],
        [WidgetKind.WeatherForecast] = ["weather", "temperature", "rain", "week", "outlook", "climate", "degrees"],
        [WidgetKind.Rss] = ["rss", "feed", "news", "headlines", "atom", "blog", "reader"],
        [WidgetKind.Stocks] = ["shares", "ticker", "market", "finance", "crypto", "bitcoin", "forex", "currency", "portfolio", "price"],
        [WidgetKind.VideoStream] = ["camera", "webcam", "cctv", "mjpeg", "live view", "printer"],
        [WidgetKind.Music] = ["spotify", "player", "playback", "song", "track", "audio", "media controls", "now playing"],
        [WidgetKind.WebSearch] = ["google", "bing", "duckduckgo", "browser", "query", "find"],

        [WidgetKind.Cpu] = ["processor", "load", "usage", "performance", "cores", "task manager", "system monitor"],
        [WidgetKind.Memory] = ["ram", "usage", "performance", "task manager", "system monitor"],
        [WidgetKind.Gpu] = ["graphics", "vram", "nvidia", "amd", "video card", "system monitor"],
        [WidgetKind.Network] = ["internet", "bandwidth", "wifi", "ethernet", "speed", "traffic", "up down"],
        [WidgetKind.Disk] = ["storage", "ssd", "hdd", "drive", "space", "free space", "io"],
        [WidgetKind.Ping] = ["latency", "network", "internet", "connection", "packet loss", "ms"],
        [WidgetKind.Processes] = ["task manager", "programs", "running", "top", "cpu", "memory", "kill"],
        [WidgetKind.Battery] = ["power", "charge", "laptop", "percent", "energy"],
        [WidgetKind.Bluetooth] = ["devices", "headphones", "wireless", "pairing"],
        [WidgetKind.ScreenTime] = ["usage", "productivity", "time tracking", "app usage", "statistics"],
        [WidgetKind.Apps] = ["launcher", "programs", "software", "start menu", "shortcuts", "open"],
        [WidgetKind.Volume] = ["sound", "audio", "speaker", "mute", "loudness"],
        [WidgetKind.Mixer] = ["sound", "audio", "per app volume", "speaker", "levels", "faders"],
        [WidgetKind.Power] = ["shutdown", "restart", "reboot", "sleep", "lock", "log off", "turn off"],
        [WidgetKind.Clipboard] = ["copy", "paste", "history", "snippets"],

        [WidgetKind.Clock] = ["time", "timezone", "watch", "world clock", "date", "analog", "digital"],
        [WidgetKind.Shortcuts] = ["link", "launcher", "bookmark", "url", "open", "pin", "quick access"],
        [WidgetKind.Notes] = ["notepad", "text", "memo", "writing", "markdown", "sticky note"],
        [WidgetKind.Scratchpad] = ["draw", "sketch", "drawing", "canvas", "whiteboard", "doodle", "pen"],
        [WidgetKind.Calculator] = ["math", "calc", "numbers", "arithmetic", "convert"],
        [WidgetKind.Gallery] = ["photos", "pictures", "images", "slideshow", "wallpaper", "bing"],
        [WidgetKind.ColorSwatch] = ["color", "colour", "swatch", "block", "accent", "spacer", "fill", "divider", "background", "gradient", "palette"],
        [WidgetKind.Countdown] = ["timer", "days until", "deadline", "event", "anniversary", "birthday"],
        [WidgetKind.Pomodoro] = ["timer", "focus", "productivity", "work break", "tomato", "session"],
        [WidgetKind.HabitTracker] = ["streaks", "daily", "routine", "goals", "checklist", "tracker"],
        [WidgetKind.MoodTracker] = ["feelings", "journal", "wellbeing", "diary", "emotions", "tracker"],
        [WidgetKind.Quote] = ["inspiration", "daily quote", "saying", "motivation"],
        [WidgetKind.SunMoon] = ["sunrise", "sunset", "moon phase", "daylight", "golden hour", "astronomy"],

        [WidgetKind.Help] = ["getting started", "guide", "docs", "documentation", "tutorial", "faq", "support"],
        [WidgetKind.Themia] = ["about", "version", "update", "app info", "license"],
        [WidgetKind.ViewSwitcher] = ["views", "pages", "desktops", "workspaces", "switch"],

        [WidgetKind.Stack] = ["container", "group", "collapse", "accordion", "combine", "layout"],
        [WidgetKind.Tabs] = ["container", "group", "switch", "combine", "layout"],
        [WidgetKind.Group] = ["container", "area", "box", "section", "frame", "collapse", "layout"],
    };

    /// <summary>
    /// The services a widget reads from, searched as their own field so "google" finds every
    /// widget that can show a Google account.
    /// </summary>
    private static readonly Dictionary<WidgetKind, string[]> ProviderNames = new()
    {
        [WidgetKind.Email] = ["Microsoft", "Google", "IMAP"],
        [WidgetKind.Calendar] = ["Microsoft", "Google", "CalDAV"],
        [WidgetKind.Contacts] = ["Microsoft", "Google", "CardDAV"],
        [WidgetKind.Tasks] = ["Microsoft", "Google"],
        [WidgetKind.OneDrive] = ["Microsoft"],
        [WidgetKind.GitHub] = ["GitHub"],
        [WidgetKind.GitHubIssues] = ["GitHub"],
        [WidgetKind.GitHubNotifications] = ["GitHub"],
        [WidgetKind.GitHubReviews] = ["GitHub"],
        [WidgetKind.ClaudeUsage] = ["Anthropic"],
    };

    /// <summary>The curated synonyms for a kind; empty for one that has none.</summary>
    public static IReadOnlyList<string> For(WidgetKind kind)
        => Keywords.TryGetValue(kind, out var words) ? words : [];

    /// <summary>The account providers a kind reads from; empty for one that needs no account.</summary>
    public static IReadOnlyList<string> Providers(WidgetKind kind)
        => ProviderNames.TryGetValue(kind, out var names) ? names : [];
}
