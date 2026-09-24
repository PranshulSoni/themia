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
        WidgetKind.Shortcuts => new ShortcutsConfig(),
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
        _ => new ClockConfig(),
    };

    public abstract WidgetConfig Clone();
}

public sealed class ClockConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Clock;

    public ClockFace Face { get; set; } = ClockFace.Digital;

    public bool ShowSeconds { get; set; }

    public bool Use24Hour { get; set; } = true;

    public bool ShowDate { get; set; } = true;

    /// <summary>
    /// IANA/Windows time-zone id to display, or null for the machine's local zone. Validated on
    /// read: an id that no longer resolves falls back to local rather than throwing at render time.
    /// </summary>
    public string? TimeZoneId { get; set; }

    public override WidgetConfig Clone() => new ClockConfig
    {
        Face = Face,
        ShowSeconds = ShowSeconds,
        Use24Hour = Use24Hour,
        ShowDate = ShowDate,
        TimeZoneId = TimeZoneId,
    };

    /// <summary>
    /// Resolve <see cref="TimeZoneId"/> to a real zone. An unknown or malformed id degrades to the
    /// local zone; it never throws, because this runs on every tick.
    /// </summary>
    public TimeZoneInfo ResolveTimeZone()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>The tick cadence this config actually needs. A clock without seconds wakes once a minute.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => ShowSeconds || Face == ClockFace.Analog
        ? TimeSpan.FromSeconds(1)
        : TimeSpan.FromMinutes(1);
}

/// <summary>Settings shared by the readout widgets that draw a bar and a number.</summary>
public abstract class GaugeConfig : WidgetConfig
{
    /// <summary>Show the recent history behind the readout.</summary>
    public bool ShowGraph { get; set; } = true;

    protected void CopyTo(GaugeConfig other) => other.ShowGraph = ShowGraph;

    /// <summary>A live meter is only honest at a one-second cadence.</summary>
    [JsonIgnore]
    public virtual TimeSpan TickInterval => TimeSpan.FromSeconds(1);
}

public sealed class CpuConfig : GaugeConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Cpu;

    public override WidgetConfig Clone()
    {
        var copy = new CpuConfig();
        CopyTo(copy);
        return copy;
    }
}

public sealed class MemoryConfig : GaugeConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Memory;

    public override WidgetConfig Clone()
    {
        var copy = new MemoryConfig();
        CopyTo(copy);
        return copy;
    }
}

public sealed class DiskConfig : GaugeConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Disk;

    /// <summary>Drive root to report, e.g. <c>C:\</c>. Empty means the system drive.</summary>
    public string Drive { get; set; } = string.Empty;

    /// <summary>
    /// Off by default, unlike the live meters.
    /// <para>
    /// Free space is sampled once a minute, so a sixty-slot window is an hour of history: the
    /// graph is a bare sliver for most of that hour and a flat line for the rest of it, because
    /// free space does not move at that cadence. The bar says the same thing immediately and
    /// truthfully. The setting stays, for a drive that really is filling up.
    /// </para>
    /// </summary>
    public DiskConfig() => ShowGraph = false;

    /// <summary>Free space moves slowly; waking every second to watch it would be waste.</summary>
    [JsonIgnore]
    public override TimeSpan TickInterval => TimeSpan.FromMinutes(1);

    public override WidgetConfig Clone()
    {
        var copy = new DiskConfig { Drive = Drive };
        CopyTo(copy);
        return copy;
    }
}

public sealed class BatteryConfig : GaugeConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Battery;

    /// <summary>Show the estimated time remaining under the percentage.</summary>
    public bool ShowTimeRemaining { get; set; } = true;

    /// <summary>Charge moves slowly, so this widget costs one wake a minute.</summary>
    [JsonIgnore]
    public override TimeSpan TickInterval => TimeSpan.FromMinutes(1);

    public override WidgetConfig Clone()
    {
        var copy = new BatteryConfig { ShowTimeRemaining = ShowTimeRemaining };
        CopyTo(copy);
        return copy;
    }
}

/// <summary>
/// Recent mail. No account settings of its own: the hearth hub owns the accounts, the sync and the
/// consent, so this only says which of the collections hearth has granted to show.
/// </summary>
public sealed class EmailConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Email;

    /// <summary>Hearth collection id, or empty for every collection the app may read.</summary>
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>Hide messages that have already been read.</summary>
    public bool UnreadOnly { get; set; }

    /// <summary>How many messages to list, 1 to 50.</summary>
    public int MaxCount { get; set; } = 10;

    /// <summary>Show the one-line preview hearth derives from the body.</summary>
    public bool ShowPreview { get; set; } = true;

    /// <summary>
    /// The list is refreshed by hearth's change events, not by polling, so the timer is only a
    /// backstop that keeps the relative timestamps ("4m", "3h") honest.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(1);

    public override WidgetConfig Clone() => new EmailConfig
    {
        CollectionId = CollectionId,
        UnreadOnly = UnreadOnly,
        MaxCount = MaxCount,
        ShowPreview = ShowPreview,
    };

    /// <summary>Clamp the count into the supported range; a persisted zero would show an empty widget.</summary>
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 1, 50);
}
