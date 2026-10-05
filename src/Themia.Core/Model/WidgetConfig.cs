using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// Per-instance widget settings. A discriminated union rather than a property bag: the
/// discriminator is written into the JSON so a config can never be silently read as the wrong
/// widget's shape.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ClockConfig), typeDiscriminator: "clock")]
[JsonDerivedType(typeof(CpuConfig), typeDiscriminator: "cpu")]
[JsonDerivedType(typeof(MemoryConfig), typeDiscriminator: "memory")]
[JsonDerivedType(typeof(DiskConfig), typeDiscriminator: "disk")]
[JsonDerivedType(typeof(BatteryConfig), typeDiscriminator: "battery")]
[JsonDerivedType(typeof(EmailConfig), typeDiscriminator: "email")]
[JsonDerivedType(typeof(ProcessesConfig), typeDiscriminator: "processes")]
[JsonDerivedType(typeof(NetworkConfig), typeDiscriminator: "network")]
[JsonDerivedType(typeof(PingConfig), typeDiscriminator: "ping")]
[JsonDerivedType(typeof(ContactsConfig), typeDiscriminator: "contacts")]
[JsonDerivedType(typeof(CalendarConfig), typeDiscriminator: "calendar")]
[JsonDerivedType(typeof(TasksConfig), typeDiscriminator: "tasks")]
[JsonDerivedType(typeof(NotesConfig), typeDiscriminator: "notes")]
[JsonDerivedType(typeof(CountdownConfig), typeDiscriminator: "countdown")]
[JsonDerivedType(typeof(PomodoroConfig), typeDiscriminator: "pomodoro")]
[JsonDerivedType(typeof(CalculatorConfig), typeDiscriminator: "calculator")]
[JsonDerivedType(typeof(SunMoonConfig), typeDiscriminator: "sunMoon")]
[JsonDerivedType(typeof(FolderConfig), typeDiscriminator: "folder")]
[JsonDerivedType(typeof(VolumeConfig), typeDiscriminator: "volume")]
[JsonDerivedType(typeof(PowerConfig), typeDiscriminator: "power")]
[JsonDerivedType(typeof(BluetoothConfig), typeDiscriminator: "bluetooth")]
[JsonDerivedType(typeof(MusicConfig), typeDiscriminator: "music")]
[JsonDerivedType(typeof(WeatherNowConfig), typeDiscriminator: "weatherNow")]
[JsonDerivedType(typeof(WeatherForecastConfig), typeDiscriminator: "weatherForecast")]
[JsonDerivedType(typeof(StocksConfig), typeDiscriminator: "stocks")]
[JsonDerivedType(typeof(RssConfig), typeDiscriminator: "rss")]
[JsonDerivedType(typeof(AppsConfig), typeDiscriminator: "apps")]
[JsonDerivedType(typeof(ShortcutsConfig), typeDiscriminator: "shortcuts")]
[JsonDerivedType(typeof(WebSearchConfig), typeDiscriminator: "webSearch")]
[JsonDerivedType(typeof(ClipboardConfig), typeDiscriminator: "clipboard")]
[JsonDerivedType(typeof(ScratchpadConfig), typeDiscriminator: "scratchpad")]
[JsonDerivedType(typeof(ColorSwatchConfig), typeDiscriminator: "colorSwatch")]
[JsonDerivedType(typeof(QuoteConfig), typeDiscriminator: "quote")]
[JsonDerivedType(typeof(GalleryConfig), typeDiscriminator: "gallery")]
[JsonDerivedType(typeof(HabitTrackerConfig), typeDiscriminator: "habitTracker")]
[JsonDerivedType(typeof(MoodTrackerConfig), typeDiscriminator: "moodTracker")]
[JsonDerivedType(typeof(GroupConfig), typeDiscriminator: "group")]
[JsonDerivedType(typeof(StackConfig), typeDiscriminator: "stack")]
[JsonDerivedType(typeof(TabsConfig), typeDiscriminator: "tabs")]
[JsonDerivedType(typeof(VideoStreamConfig), typeDiscriminator: "videoStream")]
[JsonDerivedType(typeof(GpuConfig), typeDiscriminator: "gpu")]
[JsonDerivedType(typeof(MixerConfig), typeDiscriminator: "mixer")]
[JsonDerivedType(typeof(ScreenTimeConfig), typeDiscriminator: "screenTime")]
[JsonDerivedType(typeof(HelpConfig), typeDiscriminator: "help")]
[JsonDerivedType(typeof(ThemiaConfig), typeDiscriminator: "themia")]
[JsonDerivedType(typeof(ViewSwitcherConfig), typeDiscriminator: "viewSwitcher")]
[JsonDerivedType(typeof(OneDriveConfig), typeDiscriminator: "oneDrive")]
[JsonDerivedType(typeof(GitHubConfig), typeDiscriminator: "gitHub")]
[JsonDerivedType(typeof(GitHubIssuesConfig), typeDiscriminator: "gitHubIssues")]
[JsonDerivedType(typeof(GitHubNotificationsConfig), typeDiscriminator: "gitHubNotifications")]
[JsonDerivedType(typeof(GitHubReviewsConfig), typeDiscriminator: "gitHubReviews")]
[JsonDerivedType(typeof(ClaudeUsageConfig), typeDiscriminator: "claudeUsage")]
public abstract class WidgetConfig
{
    /// <summary>The kind this config belongs to. Used to detect a config/kind mismatch on load.</summary>
    [JsonIgnore]
    public abstract WidgetKind Kind { get; }

    /// <summary>Default config for a kind. Never returns null, so a widget always has settings.</summary>
    public static WidgetConfig CreateDefault(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => new ClockConfig(),
        WidgetKind.Cpu => new CpuConfig(),
        WidgetKind.Memory => new MemoryConfig(),
        WidgetKind.Disk => new DiskConfig(),
        WidgetKind.Battery => new BatteryConfig(),
        WidgetKind.Email => new EmailConfig(),
        WidgetKind.Processes => new ProcessesConfig(),
        WidgetKind.Network => new NetworkConfig(),
        WidgetKind.Ping => new PingConfig(),
        WidgetKind.Contacts => new ContactsConfig(),
        WidgetKind.Calendar => new CalendarConfig(),
        WidgetKind.Tasks => new TasksConfig(),
        WidgetKind.Notes => new NotesConfig(),
        WidgetKind.Countdown => new CountdownConfig(),
        WidgetKind.Pomodoro => new PomodoroConfig(),
        WidgetKind.Calculator => new CalculatorConfig(),
        WidgetKind.SunMoon => new SunMoonConfig(),
        WidgetKind.Folder => new FolderConfig(),
        WidgetKind.Volume => new VolumeConfig(),
        WidgetKind.Power => new PowerConfig(),
        WidgetKind.Bluetooth => new BluetoothConfig(),
        WidgetKind.Music => new MusicConfig(),
        WidgetKind.WeatherNow => new WeatherNowConfig(),
        WidgetKind.WeatherForecast => new WeatherForecastConfig(),
        WidgetKind.Stocks => new StocksConfig(),
        WidgetKind.Rss => new RssConfig(),
        WidgetKind.Apps => new AppsConfig(),
        WidgetKind.Shortcuts => ShortcutsConfig.CreateDefault(),
        WidgetKind.WebSearch => new WebSearchConfig(),
        WidgetKind.Clipboard => new ClipboardConfig(),
        WidgetKind.Scratchpad => new ScratchpadConfig(),
        WidgetKind.ColorSwatch => new ColorSwatchConfig(),
        WidgetKind.Quote => new QuoteConfig(),
        WidgetKind.Gallery => new GalleryConfig(),
        WidgetKind.HabitTracker => new HabitTrackerConfig(),
        WidgetKind.MoodTracker => new MoodTrackerConfig(),
        WidgetKind.Group => new GroupConfig(),
        WidgetKind.Stack => new StackConfig(),
        WidgetKind.Tabs => new TabsConfig(),
        WidgetKind.VideoStream => new VideoStreamConfig(),
        WidgetKind.Gpu => new GpuConfig(),
        WidgetKind.Mixer => new MixerConfig(),
        WidgetKind.ScreenTime => new ScreenTimeConfig(),
        WidgetKind.Help => new HelpConfig(),
        WidgetKind.Themia => new ThemiaConfig(),
        WidgetKind.ViewSwitcher => new ViewSwitcherConfig(),
        WidgetKind.OneDrive => new OneDriveConfig(),
        WidgetKind.GitHub => new GitHubConfig(),
        WidgetKind.GitHubIssues => new GitHubIssuesConfig(),
        WidgetKind.GitHubNotifications => new GitHubNotificationsConfig(),
        WidgetKind.GitHubReviews => new GitHubReviewsConfig(),
        WidgetKind.ClaudeUsage => new ClaudeUsageConfig(),
        _ => new ClockConfig(),
    };

    public abstract WidgetConfig Clone();

    /// <summary>
    /// Bring a config read from disk back inside its own invariants — clamp ranges, drop values a
    /// hand edit made unusable — and say what had to change. Run once at load by
    /// <c>AppStateStore.Validate</c>, so the repairs are logged with the rest. Most configs have
    /// nothing to repair.
    /// </summary>
    public virtual IReadOnlyList<string> Repair() => [];
}
