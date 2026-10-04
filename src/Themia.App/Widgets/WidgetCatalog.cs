using Microsoft.UI.Xaml;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;

namespace Themia.App.Widgets;

/// <summary>
/// What a widget view is allowed to ask of the surface it is drawn on.
/// <para>
/// Almost no widget needs this: the settings window is the writer, and the card owns input. The two
/// that do — a note, whose text <i>is</i> its config, and the calculator, which is typed into — get
/// it through <see cref="IWidgetSurfaceAware"/> rather than through a static hook or a widened
/// <see cref="IWidgetView.Attach"/>, so nothing that does not need it has to know it exists.
/// </para>
/// </summary>
public interface IWidgetSurface
{
    /// <summary>The user's own title for this widget, or null to use the catalogue label.</summary>
    string? Label { get; }

    /// <summary>
    /// Offer a title the widget knows and the catalogue cannot: the place a Sun &amp; Moon tile is
    /// computed for, the folder a listing is showing.
    /// <para>
    /// Pushed rather than pulled, because it changes when the widget's own state changes and the
    /// card has no way to know when that is. The user's own label still wins: a title they typed
    /// is never overruled by one the widget worked out.
    /// </para>
    /// </summary>
    void SetHeaderTitle(string? title);

    /// <summary>
    /// Persist the config this view has just changed. Must not re-run layout: a relayout would push
    /// the config back into the control the user is typing in.
    /// </summary>
    void RequestSave();

    /// <summary>
    /// Ask for, or give up, the keyboard.
    /// <para>
    /// Outside edit mode the surface carries <c>WS_EX_NOACTIVATE</c>, so it never becomes the
    /// foreground window and no keystroke reaches it — a text box on it would show a caret while the
    /// typing went to whatever app actually had focus. Asking here lifts that for as long as the
    /// widget is being typed into. The cost is inherent to Win32 focus and worth naming: while you
    /// type into a widget, the window you were in loses focus.
    /// </para>
    /// <para>
    /// [S-801] This is the shell's keyboard lease, held for typing: call it from a text box's
    /// GotFocus / LostFocus. A widget that navigates with keys implements <see cref="IWidgetKeys"/>
    /// instead and gets the lease on a click; when its editor closes it keeps the arrows. The lease
    /// ends by itself on Escape, a click elsewhere and the widget going away.
    /// </para>
    /// </summary>
    void RequestTextInput(bool wanted);

    /// <summary>
    /// The display this widget is drawn on, or null where there is none — the gallery has no
    /// display, and neither does a card that has not been placed. See <see cref="IWidgetDisplay"/>.
    /// </summary>
    IWidgetDisplay? Display { get; }

    /// <summary>
    /// [S-1251] True when the card is a preview (the widget gallery) rather than a widget on the
    /// desktop. A view whose buttons act on the machine — the power tile — must be inert there.
    /// </summary>
    bool IsPreview => false;

    /// <summary>
    /// [S-873] Paint nothing, or paint again: the card drops its fill, border and content and leaves
    /// the window's hit region, so the desktop under it is clickable. For a widget that has nothing
    /// to show right now (music with nothing playing). Edit mode always shows it, so a hidden widget
    /// can still be found, moved and configured. Not persisted: the view decides it from its state.
    /// </summary>
    void SetHidden(bool hidden);
}

/// <summary>
/// What a widget may know about, and do to, the display it sits on: the views there are, which one
/// this display shows, and a way to show another.
/// <para>
/// Only the view switcher asks. It is a surface-level fact rather than widget config — the same
/// widget copied to another screen switches <i>that</i> screen — so it comes from the card host,
/// which is the one thing that knows which display a card is on.
/// </para>
/// </summary>
public interface IWidgetDisplay
{
    /// <summary>Every view, in the user's order.</summary>
    IReadOnlyList<Themia.Core.Widgets.ViewSummary> Views { get; }

    /// <summary>The view this display is showing now.</summary>
    string ShownViewId { get; }

    /// <summary>[S-19] Put a view on this display, and only this one.</summary>
    void ShowView(string viewId);

    /// <summary>
    /// [S-1656] The rects of a view's widgets on the desktop, for a sketch of its layout: top-level
    /// widgets only (a container's members sit inside it). Empty for a view that does not exist.
    /// </summary>
    IReadOnlyList<Themia.Core.Geometry.FracRect> Tiles(string viewId);

    /// <summary>[S-1656] This display's width over its height, the shape of a sketch; 0 when not known.</summary>
    double Aspect { get; }

    /// <summary>[S-1650] Duplicate a view and show the copy on this display ([S-900]).</summary>
    void DuplicateView(string viewId);

    /// <summary>[S-1650] Move a view one place earlier (-1) or later (+1) in the order ([S-901]).</summary>
    void MoveView(string viewId, int delta);

    /// <summary>[S-1650] Delete a view (never the last), with the undo notice ([S-881], [S-902]).</summary>
    void DeleteView(string viewId);

    /// <summary>[S-1650] Open the view's settings page: its name and icon ([S-903]).</summary>
    void OpenViewSettings(string viewId);
}

/// <summary>Implemented by the few views that edit their own config or take typing.</summary>
public interface IWidgetSurfaceAware
{
    void Bind(IWidgetSurface surface);
}

/// <summary>
/// What a view puts on the right of the header the card draws for it — an unread badge, a count,
/// a busy ring.
/// <para>
/// The header itself belongs to the card, not to the view. Twenty-odd widgets each building their
/// own meant twenty-odd chances to disagree about its size, colour, spacing and behaviour, and it
/// left nowhere to put a setting like "draw it at the bottom instead" without asking every one of
/// them to honour it. The card knows the icon and the title already — the catalogue has one and the
/// instance has the other — so the only thing it cannot know is this.
/// </para>
/// </summary>
public interface IWidgetHeaderContent
{
    /// <summary>The trailing element, or null. Read once when the card builds its header.</summary>
    FrameworkElement? HeaderTrailing { get; }
}

/// <summary>
/// A view that holds other widgets.
/// <para>
/// The container does not create its children — the surface does, exactly as it creates every other
/// card, so a widget inside a stack is the same object with the same lifetime as one on the desktop
/// and nothing has to be special-cased to keep it ticking. What a container gets is the finished
/// cards and the job of arranging them.
/// </para>
/// </summary>
public interface IWidgetContainerView
{
    /// <summary>
    /// Take the children this container now holds, in view order. Called on every layout pass, so
    /// it must be cheap when nothing changed and must not restart anything the children are doing.
    /// </summary>
    void SetChildren(IReadOnlyList<FrameworkElement> children);
}

/// <summary>What every widget view can do. Kept tiny on purpose: the card owns chrome and input.</summary>
public interface IWidgetView
{
    /// <summary>Bind the view to the shared ticker and its initial config.</summary>
    void Attach(Ticker ticker, WidgetConfig config);

    /// <summary>Re-read a config the user just changed.</summary>
    void ApplyConfig(WidgetConfig config);

    /// <summary>Release timers and handles. Must be safe to call more than once.</summary>
    void Detach();
}

/// <summary>One entry in the widget picker.</summary>
/// <param name="Kind">The persisted discriminator.</param>
/// <param name="Label">Display name.</param>
/// <param name="Description">One line shown under the name in the picker.</param>
/// <param name="Glyph">Segoe Fluent Icons code point, so the picker matches the rest of Windows.</param>
/// <param name="DefaultWidthPx">[S-10] Default size in device-independent pixels, converted to a fraction at drop time.</param>
/// <param name="Category">[S-420] The picker column section it is listed under.</param>
internal readonly record struct WidgetDefinition(
    WidgetKind Kind,
    string Label,
    string Description,
    string Glyph,
    double DefaultWidthPx,
    double DefaultHeightPx,
    WidgetCategory Category);

/// <summary>
/// [S-420] The sections of the widget picker, in the order they are read. A closed set, and an
/// enum rather than a string on each definition, so a typo cannot quietly invent a tenth section.
/// </summary>
internal enum WidgetCategory
{
    Time,
    Accounts,
    System,
    Information,
    Media,
    Launch,
    Desk,
    Trackers,
    Layout,

    /// <summary>GitHub and Claude usage: the widgets for people who write code.</summary>
    Developer,

    /// <summary>About the app itself: the guide and the mark.</summary>
    App,
}

/// <summary>
/// The widget registry. One place that maps a <see cref="WidgetKind"/> to its metadata and its view,
/// so adding a widget is one entry here plus one enum member — there is no second list to forget.
/// </summary>
internal static class WidgetCatalog
{
    public static readonly IReadOnlyList<WidgetDefinition> All =
    [
        new(WidgetKind.Clock, "Clock", "Time and date, digital or analog", Glyphs.Clock, 280, 140, WidgetCategory.Time),
        new(WidgetKind.Cpu, "CPU", "Processor load across all cores", Glyphs.Cpu, 200, 150, WidgetCategory.System),
        new(WidgetKind.Memory, "Memory", "Physical memory in use", Glyphs.Memory, 200, 150, WidgetCategory.System),
        new(WidgetKind.Disk, "Disk", "Read and write activity, and space left", Glyphs.Disk, 200, 150, WidgetCategory.System),
        new(WidgetKind.Battery, "Battery", "Charge level and time remaining", Glyphs.Battery, 200, 150, WidgetCategory.System),
        new(WidgetKind.Network, "Network", "Bytes per second across the active adapters", Glyphs.Network, 200, 150, WidgetCategory.System),
        new(WidgetKind.Ping, "Ping", "Round-trip time to a host", Glyphs.Ping, 200, 150, WidgetCategory.System),
        new(WidgetKind.Processes, "Processes", "The busiest processes, ranked", Glyphs.Processes, 320, 240, WidgetCategory.System),
        new(WidgetKind.Gpu, "GPU", "Graphics load and video memory", Glyphs.Gpu, 220, 220, WidgetCategory.System),
        new(WidgetKind.Mixer, "Mixer", "Volume for each app that plays sound", Glyphs.Mixer, 300, 260, WidgetCategory.System),
        new(WidgetKind.ScreenTime, "Screen time", "Which apps you used today, and for how long", Glyphs.ScreenTime, 260, 300, WidgetCategory.System),
        new(WidgetKind.Email, "Mail", "Recent messages from Outlook, Gmail or any mail server", Glyphs.Mail, 380, 260, WidgetCategory.Accounts),
        new(WidgetKind.Calendar, "Calendar", "Your Microsoft, Google and CalDAV calendars, with birthdays", Glyphs.Calendar, 340, 260, WidgetCategory.Accounts),
        new(WidgetKind.Tasks, "Tasks", "Microsoft To Do, Google Tasks, or a list on this PC", Glyphs.Task, 320, 320, WidgetCategory.Accounts),
        new(WidgetKind.Contacts, "Contacts", "People from your Microsoft, Google and CardDAV address books", Glyphs.Contact, 320, 260, WidgetCategory.Accounts),
        new(WidgetKind.Notes, "Notes", "A note kept on this machine", Glyphs.Note, 300, 220, WidgetCategory.Desk),
        new(WidgetKind.Countdown, "Countdown", "Time left until the dates you care about", Glyphs.Countdown, 300, 200, WidgetCategory.Time),
        new(WidgetKind.Pomodoro, "Pomodoro", "A focus and break timer", Glyphs.Pomodoro, 240, 200, WidgetCategory.Time),
        new(WidgetKind.Calculator, "Calculator", "An arithmetic scratchpad", Glyphs.Calculator, 260, 260, WidgetCategory.Desk),
        new(WidgetKind.SunMoon, "Sun & Moon", "Sunrise, sunset and the moon phase", Glyphs.Sun, 320, 240, WidgetCategory.Time),
        new(WidgetKind.Folder, "Folder", "What is in a folder on this PC", Glyphs.Folder, 340, 280, WidgetCategory.Launch),
        new(WidgetKind.Volume, "Volume", "Master output level and mute", Glyphs.Volume, 200, 150, WidgetCategory.System),
        new(WidgetKind.Power, "Power", "Shut down, restart, sleep and lock", Glyphs.Power, 260, 120, WidgetCategory.System),
        new(WidgetKind.Bluetooth, "Bluetooth", "Paired devices and their battery", Glyphs.Bluetooth, 300, 240, WidgetCategory.System),
        new(WidgetKind.Music, "Now playing", "Whatever Windows is playing", Glyphs.Music, 340, 160, WidgetCategory.Media),
        new(WidgetKind.WeatherNow, "Weather", "Conditions where you are", Glyphs.Weather, 260, 200, WidgetCategory.Information),
        new(WidgetKind.WeatherForecast, "Forecast", "The next few days", Glyphs.Forecast, 380, 220, WidgetCategory.Information),
        new(WidgetKind.Stocks, "Stocks", "Tickers and crypto, with a sparkline", Glyphs.Stocks, 320, 240, WidgetCategory.Information),
        new(WidgetKind.Rss, "Feed", "Headlines from the feeds you choose", Glyphs.Rss, 360, 280, WidgetCategory.Information),
        new(WidgetKind.Apps, "Apps", "Launch an installed application", Glyphs.Apps, 320, 240, WidgetCategory.Launch),
        new(WidgetKind.Shortcuts, "Shortcuts", "Links, files and folders you keep to hand", Glyphs.Shortcut, 320, 220, WidgetCategory.Launch),
        new(WidgetKind.OneDrive, "OneDrive", "Recent files, or a folder, on your OneDrive", Glyphs.OneDrive, 330, 320, WidgetCategory.Launch),
        new(WidgetKind.WebSearch, "Search", "Type a query, open it in your browser", Glyphs.Search, 320, 56, WidgetCategory.Launch),
        new(WidgetKind.Clipboard, "Clipboard", "What you have copied recently", Glyphs.Clipboard, 320, 260, WidgetCategory.Desk),
        new(WidgetKind.Scratchpad, "Scratchpad", "A place to draw", Glyphs.Scratchpad, 300, 260, WidgetCategory.Desk),
        new(WidgetKind.ColorSwatch, "Colour", "A block of colour or a gradient, or a palette to copy from", Glyphs.Color, 120, 120, WidgetCategory.Desk),
        new(WidgetKind.Quote, "Quote", "A line worth rereading", Glyphs.Quote, 320, 180, WidgetCategory.Desk),
        new(WidgetKind.Gallery, "Gallery", "Pictures from a folder, a file or Bing", Glyphs.Gallery, 320, 240, WidgetCategory.Media),
        new(WidgetKind.VideoStream, "Video stream", "A live camera: MJPEG, a snapshot, MP4 or HLS", Glyphs.Video, 320, 240, WidgetCategory.Media),
        new(WidgetKind.WebElement, "Web element", "One part of a web page, kept up to date", Glyphs.WebElement, 360, 260, WidgetCategory.Information),
        new(WidgetKind.HabitTracker, "Habits", "Daily check-offs and streaks", Glyphs.Habit, 320, 240, WidgetCategory.Trackers),
        new(WidgetKind.MoodTracker, "Mood", "How you felt, as a heatmap", Glyphs.Mood, 320, 240, WidgetCategory.Trackers),
        new(WidgetKind.Group, "Group", "An area that holds other widgets", Glyphs.Group, 400, 300, WidgetCategory.Layout),
        new(WidgetKind.Stack, "Stack", "Widgets rolled up to their headers", Glyphs.Stack, 360, 320, WidgetCategory.Layout),
        new(WidgetKind.Tabs, "Tabs", "Several widgets, one at a time", Glyphs.Tabs, 360, 320, WidgetCategory.Layout),
        new(WidgetKind.ViewSwitcher, "View switcher", "Switch the view of the display it is on", Glyphs.View, 320, 56, WidgetCategory.Layout),
        new(WidgetKind.GitHub, "GitHub", "A repository: stars, CI, commits, pull requests", Glyphs.Code, 320, 320, WidgetCategory.Developer),
        new(WidgetKind.GitHubIssues, "Assigned issues", "Open GitHub issues assigned to you", Glyphs.Issue, 320, 260, WidgetCategory.Developer),
        new(WidgetKind.GitHubNotifications, "Notifications", "Your GitHub notifications", Glyphs.Bell, 320, 340, WidgetCategory.Developer),
        new(WidgetKind.GitHubReviews, "Review requests", "Pull requests waiting for your review", Glyphs.PullRequest, 320, 260, WidgetCategory.Developer),
        new(WidgetKind.ClaudeUsage, "Claude usage", "Your Claude plan's session and weekly limits", Glyphs.Gauge, 260, 180, WidgetCategory.Developer),
        new(WidgetKind.Help, "Help", "How Themia works, in four short tabs", Glyphs.Help, 440, 440, WidgetCategory.App),
        new(WidgetKind.Themia, "Themia", "The Themia mark, name and version", Glyphs.Themia, 120, 120, WidgetCategory.App),
    ];

    /// <summary>The heading a picker section is shown under.</summary>
    public static string Title(WidgetCategory category) => category switch
    {
        WidgetCategory.Time => "Time",
        WidgetCategory.Accounts => "Mail & calendar",
        WidgetCategory.System => "System",
        WidgetCategory.Information => "Information",
        WidgetCategory.Media => "Media",
        WidgetCategory.Launch => "Launch",
        WidgetCategory.Desk => "Desk",
        WidgetCategory.Trackers => "Trackers",
        WidgetCategory.Layout => "Layout",
        WidgetCategory.Developer => "Developer",
        WidgetCategory.App => "Themia",
        _ => category.ToString(),
    };

    public static WidgetDefinition Definition(WidgetKind kind)
    {
        foreach (var definition in All)
        {
            if (definition.Kind == kind)
            {
                return definition;
            }
        }

        // Unreachable for a validated state, but a total function beats an exception on a render path.
        return All[0];
    }

    /// <summary>Build the view for a kind. Returns a <see cref="FrameworkElement"/> that also implements <see cref="IWidgetView"/>.</summary>
    public static FrameworkElement CreateView(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => new ClockView(),
        WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Disk
            or WidgetKind.Battery or WidgetKind.Network or WidgetKind.Ping => new MeterView(kind),
        WidgetKind.Processes => new ProcessesView(),
        WidgetKind.Gpu => new GpuView(),
        WidgetKind.Mixer => new MixerView(),
        WidgetKind.ScreenTime => new ScreenTimeView(),
        WidgetKind.Email => new EmailView(),
        WidgetKind.Contacts => new ContactsView(),
        WidgetKind.Calendar => new CalendarView(),
        WidgetKind.Tasks => new TasksView(),
        WidgetKind.Notes => new NotesView(),
        WidgetKind.Countdown => new CountdownView(),
        WidgetKind.Pomodoro => new PomodoroView(),
        WidgetKind.Calculator => new CalculatorView(),
        WidgetKind.SunMoon => new SunMoonView(),
        WidgetKind.Folder => new FolderView(),
        WidgetKind.Volume => new VolumeView(),
        WidgetKind.Power => new PowerView(),
        WidgetKind.Bluetooth => new BluetoothView(),
        WidgetKind.Music => new MusicView(),
        WidgetKind.WeatherNow => new WeatherNowView(),
        WidgetKind.WeatherForecast => new WeatherForecastView(),
        WidgetKind.Stocks => new StocksView(),
        WidgetKind.Rss => new RssView(),
        WidgetKind.Apps => new AppsView(),
        WidgetKind.Shortcuts => new ShortcutsView(),
        WidgetKind.OneDrive => new OneDriveView(),
        WidgetKind.WebSearch => new WebSearchView(),
        WidgetKind.Clipboard => new ClipboardView(),
        WidgetKind.Scratchpad => new ScratchpadView(),
        WidgetKind.ColorSwatch => new ColorSwatchView(),
        WidgetKind.Quote => new QuoteView(),
        WidgetKind.Gallery => new GalleryView(),
        WidgetKind.VideoStream => new VideoStreamView(),
        WidgetKind.WebElement => new WebElementView(),
        WidgetKind.HabitTracker => new HabitTrackerView(),
        WidgetKind.MoodTracker => new MoodTrackerView(),
        WidgetKind.Group => new GroupView(),
        WidgetKind.Stack => new StackView(),
        WidgetKind.Tabs => new TabsView(),
        WidgetKind.Help => new HelpView(),
        WidgetKind.Themia => new ThemiaView(),
        WidgetKind.GitHub => new GitHubView(),
        WidgetKind.GitHubIssues or WidgetKind.GitHubReviews => new GitHubSearchView(kind),
        WidgetKind.GitHubNotifications => new GitHubNotificationsView(),
        WidgetKind.ClaudeUsage => new ClaudeUsageView(),
        WidgetKind.ViewSwitcher => new ViewSwitcherView(),
        _ => new ClockView(),
    };
}
